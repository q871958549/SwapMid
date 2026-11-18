using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SwapMid.Infrastructure;
using SwapMid.Monitoring;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// Apache Delta Lake 事务日志（<c>_delta_log/00000000000000000000.json</c>）写入器：
/// 为 Parquet CDC 分片与小文件压缩合并提交符合 Delta Protocol v1/v2 规范的 NDJSON 动作记录
/// （<c>commitInfo</c>、<c>protocol</c>、<c>metaData</c>、<c>add</c>、<c>remove</c>），
/// 并利用 S3 <c>If-None-Match: *</c> 实现乐观并发提交与 RunId 幂等恢复。
/// </summary>
internal static class DeltaLakeCommitWriter
{
    private const int MaxOptimisticRetries = 64;

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    internal static readonly string DeltaSchemaJson = BuildCdcParquetDeltaSchemaJson();

    internal static async Task<long> CommitAppendAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        CdcStreamDefinition stream,
        CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument latestDoc,
        Guid runId,
        string dataObjectKey,
        long byteSize,
        long rowCount,
        bool isInitialSnapshot,
        DateTime committedAtUtc,
        CancellationToken cancellationToken)
    {
        var relPath = config.ToRelativeTablePath(destination, dataObjectKey);
        var tsMs = new DateTimeOffset(DateTime.SpecifyKind(committedAtUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var statsJson = JsonSerializer.Serialize(new { numRecords = rowCount }, CompactJson);
        var startVersion = Math.Max(0, (latestDoc.LatestDeltaVersion ?? -1) + 1);

        return await CommitWithOptimisticConcurrencyAsync(
            httpClientFactory,
            configuration,
            config,
            destination,
            runId,
            startVersion,
            version => BuildAppendNdjson(
                version,
                stream,
                runId,
                relPath,
                byteSize,
                tsMs,
                statsJson,
                isInitialSnapshot),
            cancellationToken);
    }

    internal static async Task<long> CommitOptimizeCompactionAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        CdcStreamDefinition stream,
        CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument latestDoc,
        Guid compactRunId,
        IReadOnlyList<string> removedObjectKeys,
        string addedObjectKey,
        long outputByteSize,
        long totalRowCount,
        DateTime committedAtUtc,
        CancellationToken cancellationToken)
    {
        var addedRelPath = config.ToRelativeTablePath(destination, addedObjectKey);
        var removedRelPaths = removedObjectKeys
            .Where(k => !string.Equals(k, addedObjectKey, StringComparison.Ordinal))
            .Select(k => config.ToRelativeTablePath(destination, k))
            .ToList();
        var tsMs = new DateTimeOffset(DateTime.SpecifyKind(committedAtUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var statsJson = JsonSerializer.Serialize(new { numRecords = totalRowCount }, CompactJson);
        var startVersion = Math.Max(0, (latestDoc.LatestDeltaVersion ?? -1) + 1);

        return await CommitWithOptimisticConcurrencyAsync(
            httpClientFactory,
            configuration,
            config,
            destination,
            compactRunId,
            startVersion,
            version => BuildOptimizeNdjson(
                version,
                stream,
                compactRunId,
                removedRelPaths,
                addedRelPath,
                outputByteSize,
                tsMs,
                statsJson),
            cancellationToken);
    }

    internal static string BuildAppendNdjson(
        long version,
        CdcStreamDefinition stream,
        Guid runId,
        string relativeDataPath,
        long byteSize,
        long timestampMs,
        string statsJson,
        bool isInitialSnapshot)
    {
        var lines = new List<string>(4)
        {
            SerializeCommitInfo(
                version,
                operation: "STREAMING UPDATE",
                swapmidRunId: runId,
                timestampMs: timestampMs,
                isBlindAppend: true,
                operationParameters: new Dictionary<string, string>
                {
                    ["outputMode"] = isInitialSnapshot ? "SnapshotAppend" : "Update",
                    ["swapmidStreamId"] = stream.Id.ToString("D"),
                    ["swapmidRunId"] = runId.ToString("D"),
                }),
        };

        if (version == 0)
        {
            lines.Add(SerializeProtocol());
            lines.Add(SerializeMetaData(stream, timestampMs));
        }

        lines.Add(JsonSerializer.Serialize(new
        {
            add = new
            {
                path = relativeDataPath,
                partitionValues = new Dictionary<string, string>(),
                size = byteSize,
                modificationTime = timestampMs,
                dataChange = true,
                stats = statsJson,
            },
        }, CompactJson));

        return string.Join('\n', lines) + "\n";
    }

    internal static string BuildOptimizeNdjson(
        long version,
        CdcStreamDefinition stream,
        Guid compactRunId,
        IReadOnlyList<string> removedRelativePaths,
        string addedRelativePath,
        long outputByteSize,
        long timestampMs,
        string statsJson)
    {
        var lines = new List<string>(removedRelativePaths.Count + 4)
        {
            SerializeCommitInfo(
                version,
                operation: "OPTIMIZE",
                swapmidRunId: compactRunId,
                timestampMs: timestampMs,
                isBlindAppend: false,
                operationParameters: new Dictionary<string, string>
                {
                    ["predicate"] = "[]",
                    ["zOrderBy"] = "[]",
                    ["swapmidStreamId"] = stream.Id.ToString("D"),
                    ["swapmidRunId"] = compactRunId.ToString("D"),
                }),
        };

        if (version == 0)
        {
            lines.Add(SerializeProtocol());
            lines.Add(SerializeMetaData(stream, timestampMs));
        }

        foreach (var removed in removedRelativePaths)
        {
            lines.Add(JsonSerializer.Serialize(new
            {
                remove = new
                {
                    path = removed,
                    deletionTimestamp = timestampMs,
                    dataChange = false,
                    extendedFileMetadata = true,
                },
            }, CompactJson));
        }

        lines.Add(JsonSerializer.Serialize(new
        {
            add = new
            {
                path = addedRelativePath,
                partitionValues = new Dictionary<string, string>(),
                size = outputByteSize,
                modificationTime = timestampMs,
                dataChange = false,
                stats = statsJson,
            },
        }, CompactJson));

        return string.Join('\n', lines) + "\n";
    }

    private static async Task<long> CommitWithOptimisticConcurrencyAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        Guid runId,
        long startVersion,
        Func<long, string> buildPayload,
        CancellationToken cancellationToken)
    {
        var runToken = runId.ToString("D");

        for (var attempt = 0; attempt < MaxOptimisticRetries; attempt++)
        {
            var version = startVersion + attempt;
            var commitKey = config.BuildDeltaCommitKey(destination, version);

            var existing = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
                httpClientFactory,
                configuration,
                config,
                commitKey,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                if (existing.Contains(runToken, StringComparison.OrdinalIgnoreCase))
                {
                    return version;
                }

                continue;
            }

            var payload = buildPayload(version);
            var status = await PutDeltaCommitIfAbsentAsync(
                httpClientFactory,
                configuration,
                config,
                commitKey,
                payload,
                cancellationToken);
            if (status)
            {
                return version;
            }

            var raced = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
                httpClientFactory,
                configuration,
                config,
                commitKey,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(raced) &&
                raced.Contains(runToken, StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }
        }

        throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
            .WithData("Reason", "Delta Lake _delta_log 并发版本号竞争超出最大重试次数，请重试提交。");
    }

    private static async Task<bool> PutDeltaCommitIfAbsentAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string commitKey,
        string ndjsonContent,
        CancellationToken cancellationToken)
    {
        var objectUri = config.BuildObjectUri(commitKey);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
            objectUri,
            configuration,
            cancellationToken);

        var bytes = Encoding.UTF8.GetBytes(ndjsonContent);
        var payloadHashHex = AwsSigV4Signer.ComputeSha256Hex(bytes);

        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
        content.Headers.ContentLength = bytes.Length;

        using var request = new HttpRequestMessage(HttpMethod.Put, objectUri) { Content = content };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        AwsSigV4Signer.Sign(
            request,
            config.Region,
            config.AccessKey,
            config.SecretKey ?? string.Empty,
            payloadHashHex);

        var client = httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            return false;
        }

        var err = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
            .WithData(
                "Reason",
                SinkConfigContractHelpers.RedactDiagnosticText(
                    $"Delta Lake _delta_log PUT 失败 ({(int)response.StatusCode}): {err}"));
    }

    private static string SerializeProtocol()
        => JsonSerializer.Serialize(new
        {
            protocol = new
            {
                minReaderVersion = 1,
                minWriterVersion = 2,
            },
        }, CompactJson);

    private static string SerializeMetaData(CdcStreamDefinition stream, long createdTimeMs)
        => JsonSerializer.Serialize(new
        {
            metaData = new
            {
                id = stream.Id.ToString("D"),
                name = stream.Name,
                format = new
                {
                    provider = "parquet",
                    options = new Dictionary<string, string>(),
                },
                schemaString = DeltaSchemaJson,
                partitionColumns = Array.Empty<string>(),
                configuration = new Dictionary<string, string>(),
                createdTime = createdTimeMs,
            },
        }, CompactJson);

    private static string SerializeCommitInfo(
        long version,
        string operation,
        Guid swapmidRunId,
        long timestampMs,
        bool isBlindAppend,
        Dictionary<string, string> operationParameters)
        => JsonSerializer.Serialize(new
        {
            commitInfo = new
            {
                timestamp = timestampMs,
                operation,
                operationParameters,
                readVersion = version > 0 ? version - 1 : (long?)null,
                isolationLevel = "Serializable",
                isBlindAppend,
                engineInfo = "SwapMid-2.0.0",
                txnId = swapmidRunId.ToString("D"),
            },
        }, CompactJson);

    private static string BuildCdcParquetDeltaSchemaJson()
    {
        var fields = new object[]
        {
            Field("event_id", "string"),
            Field("stream_id", "string"),
            Field("stream_name", "string"),
            Field("source_schema", "string"),
            Field("source_table", "string"),
            Field("op", "string"),
            Field("lsn", "string"),
            Field("commit_timestamp_utc", "timestamp"),
            Field("changed_columns", "string"),
            Field("after_json", "string"),
            Field("before_json", "string"),
        };

        return JsonSerializer.Serialize(new
        {
            type = "struct",
            fields,
        }, CompactJson);

        static object Field(string name, string type) => new
        {
            name,
            type,
            nullable = true,
            metadata = new Dictionary<string, object>(),
        };
    }
}
