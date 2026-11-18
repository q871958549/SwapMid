using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NpgsqlTypes;
using SwapMid.Infrastructure;
using SwapMid.Monitoring;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// CDC 对象存储（S3/MinIO Parquet &amp; JSONL）Lakehouse Manifest 与检查点索引写入器：
/// 在数据分片上传成功后幂等写入不可变批次 Manifest（_swapmid/manifests/*.json）与最新水位线索引（_swapmid/latest.json）。
/// </summary>
internal static class CdcObjectStorageLakehouseWriter
{
    private const int MaxRecentFilesInLatest = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal sealed class CdcBatchManifestDocument
    {
        public int ManifestVersion { get; set; } = 1;
        public Guid StreamId { get; set; }
        public string StreamName { get; set; } = string.Empty;
        public Guid RunId { get; set; }
        public bool IsInitialSnapshot { get; set; }
        public string Format { get; set; } = "parquet";
        public string DataObjectKey { get; set; } = string.Empty;
        public long ByteSize { get; set; }
        public string Sha256Hex { get; set; } = string.Empty;
        public long RowCount { get; set; }
        public string? StartLsn { get; set; }
        public string? EndLsn { get; set; }
        public DateTime? MinCommitTimestampUtc { get; set; }
        public DateTime? MaxCommitTimestampUtc { get; set; }
        public string? SchemaBaselineHash { get; set; }
        public string? MonitoredTables { get; set; }
        public Dictionary<string, long> OpCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> TableCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime CommittedAtUtc { get; set; }
    }

    internal sealed class CdcLatestCheckpointDocument
    {
        public int ManifestVersion { get; set; } = 1;
        public Guid StreamId { get; set; }
        public string StreamName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Format { get; set; } = "parquet";
        public string? CheckpointLsn { get; set; }
        public string? SchemaBaselineHash { get; set; }
        public string LatestManifestKey { get; set; } = string.Empty;
        public string LatestDataObjectKey { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
        public List<CdcLatestDataFileEntry> RecentFiles { get; set; } = new();
    }

    internal sealed class CdcLatestDataFileEntry
    {
        public Guid RunId { get; set; }
        public string ManifestKey { get; set; } = string.Empty;
        public string DataObjectKey { get; set; } = string.Empty;
        public string Format { get; set; } = "parquet";
        public long RowCount { get; set; }
        public long ByteSize { get; set; }
        public string Sha256Hex { get; set; } = string.Empty;
        public bool IsInitialSnapshot { get; set; }
        public string? EndLsn { get; set; }
        public DateTime CommittedAtUtc { get; set; }
    }

    internal static async Task<long> DeliverWithManifestAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        string destination,
        IReadOnlyList<PgOutputCapturedChange> changes,
        NpgsqlLogSequenceNumber? batchEndLsn,
        ObjectStorageEtlSinkWriter writer,
        string[] columns,
        Type[] clrTypes,
        List<object?[]> rows,
        string?[]? partitionKeys,
        CancellationToken cancellationToken,
        string? idempotencyKeyOverride = null)
    {
        var endLsnText = idempotencyKeyOverride ?? batchEndLsn?.ToString() ?? changes[^1].Lsn;
        var runId = CdcProtocolHelpers.BuildDeterministicPipelineRunId(stream.Id, endLsnText);

        var uploaded = await writer.WriteAndUploadCdcBatchAsync(
            runId,
            targetSink,
            destination,
            async (consumeBatchAsync, ct) =>
            {
                var batch = new EtlSourceBatch(columns, clrTypes, rows, batchIndex: 0, partitionKeys: partitionKeys);
                await consumeBatchAsync(batch, ct);
            },
            cancellationToken);

        if (uploaded.RowCount == 0)
        {
            return 0;
        }

        var config = ObjectStorageSinkConfigContract.ParseForWrite(targetSink.Config, requireSecretKey: true);
        var committedAtUtc = DateTime.UtcNow;
        var isInitialSnapshot = !string.IsNullOrWhiteSpace(idempotencyKeyOverride);
        var lsnToken = BuildSafeLsnToken(batchEndLsn?.ToString() ?? changes[^1].Lsn, isInitialSnapshot);
        var manifestKey = config.BuildBatchManifestKey(destination, lsnToken, runId);

        var opCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var tableCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        DateTime? minTs = null;
        DateTime? maxTs = null;

        foreach (var change in changes)
        {
            opCounts[change.Operation] = opCounts.GetValueOrDefault(change.Operation) + 1;
            var tableKey = $"{change.Schema}.{change.Table}";
            tableCounts[tableKey] = tableCounts.GetValueOrDefault(tableKey) + 1;

            var ts = change.CommitTimestampUtc.ToUniversalTime();
            if (!minTs.HasValue || ts < minTs.Value) minTs = ts;
            if (!maxTs.HasValue || ts > maxTs.Value) maxTs = ts;
        }

        var manifest = new CdcBatchManifestDocument
        {
            StreamId = stream.Id,
            StreamName = stream.Name,
            RunId = runId,
            IsInitialSnapshot = isInitialSnapshot,
            Format = uploaded.Format,
            DataObjectKey = uploaded.ObjectKey,
            ByteSize = uploaded.ByteSize,
            Sha256Hex = uploaded.Sha256Hex,
            RowCount = uploaded.RowCount,
            StartLsn = changes[0].Lsn,
            EndLsn = batchEndLsn?.ToString() ?? changes[^1].Lsn,
            MinCommitTimestampUtc = minTs,
            MaxCommitTimestampUtc = maxTs,
            SchemaBaselineHash = stream.SchemaBaselineHash,
            MonitoredTables = stream.MonitoredTables,
            OpCounts = opCounts,
            TableCounts = tableCounts,
            CommittedAtUtc = committedAtUtc,
        };

        await PutJsonObjectAsync(
            writer.HttpClientFactory,
            writer.Configuration,
            config,
            manifestKey,
            JsonSerializer.Serialize(manifest, JsonOptions),
            cancellationToken);

        var latestKey = config.BuildLatestManifestKey(destination);
        var latestDoc = await LoadOrInitializeLatestAsync(
            writer.HttpClientFactory,
            writer.Configuration,
            config,
            latestKey,
            stream,
            destination,
            uploaded.Format,
            cancellationToken);
        latestDoc.StreamName = stream.Name;
        latestDoc.Format = uploaded.Format;
        latestDoc.CheckpointLsn = manifest.EndLsn ?? latestDoc.CheckpointLsn;
        latestDoc.SchemaBaselineHash = stream.SchemaBaselineHash ?? latestDoc.SchemaBaselineHash;
        latestDoc.LatestManifestKey = manifestKey;
        latestDoc.LatestDataObjectKey = uploaded.ObjectKey;
        latestDoc.UpdatedAtUtc = committedAtUtc;

        latestDoc.RecentFiles.RemoveAll(f => string.Equals(f.ManifestKey, manifestKey, StringComparison.Ordinal));
        latestDoc.RecentFiles.Add(new CdcLatestDataFileEntry
        {
            RunId = runId,
            ManifestKey = manifestKey,
            DataObjectKey = uploaded.ObjectKey,
            Format = uploaded.Format,
            RowCount = uploaded.RowCount,
            ByteSize = uploaded.ByteSize,
            Sha256Hex = uploaded.Sha256Hex,
            IsInitialSnapshot = isInitialSnapshot,
            EndLsn = manifest.EndLsn,
            CommittedAtUtc = committedAtUtc,
        });
        if (latestDoc.RecentFiles.Count > MaxRecentFilesInLatest)
        {
            latestDoc.RecentFiles = latestDoc.RecentFiles
                .Skip(latestDoc.RecentFiles.Count - MaxRecentFilesInLatest)
                .ToList();
        }

        await PutJsonObjectAsync(
            writer.HttpClientFactory,
            writer.Configuration,
            config,
            latestKey,
            JsonSerializer.Serialize(latestDoc, JsonOptions),
            cancellationToken);

        PlatformMetrics.CdcObjectStorageManifestsTotal.WithLabels(uploaded.Format).Inc();
        return uploaded.RowCount;
    }

    private static async Task<CdcLatestCheckpointDocument> LoadOrInitializeLatestAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string latestKey,
        CdcStreamDefinition stream,
        string destination,
        string format,
        CancellationToken cancellationToken)
    {
        var existingJson = await TryGetObjectStorageJsonAsync(
            httpClientFactory,
            configuration,
            config,
            latestKey,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<CdcLatestCheckpointDocument>(existingJson, JsonOptions);
                if (parsed != null && parsed.StreamId == stream.Id)
                {
                    parsed.RecentFiles ??= new List<CdcLatestDataFileEntry>();
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // Corrupt legacy file: rebuild fresh bounded checkpoint index cleanly.
            }
        }

        return new CdcLatestCheckpointDocument
        {
            StreamId = stream.Id,
            StreamName = stream.Name,
            Destination = destination,
            Format = format,
        };
    }

    internal static async Task PutJsonObjectAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string objectKey,
        string jsonContent,
        CancellationToken cancellationToken)
    {
        var objectUri = config.BuildObjectUri(objectKey);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
            objectUri,
            configuration,
            cancellationToken);

        var bytes = Encoding.UTF8.GetBytes(jsonContent);
        var payloadHashHex = AwsSigV4Signer.ComputeSha256Hex(bytes);

        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentLength = bytes.Length;

        using var request = new HttpRequestMessage(HttpMethod.Put, objectUri) { Content = content };
        AwsSigV4Signer.Sign(
            request,
            config.Region,
            config.AccessKey,
            config.SecretKey ?? string.Empty,
            payloadHashHex);

        var client = httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                .WithData(
                    "Reason",
                    SinkConfigContractHelpers.RedactDiagnosticText(
                        $"S3/MinIO Manifest PUT 失败 ({(int)response.StatusCode}): {err}"));
        }
    }

    internal static async Task<string?> TryGetObjectStorageJsonAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string objectKey,
        CancellationToken cancellationToken)
    {
        var objectUri = config.BuildObjectUri(objectKey);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
            objectUri,
            configuration,
            cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, objectUri);
        AwsSigV4Signer.Sign(
            request,
            config.Region,
            config.AccessKey,
            config.SecretKey ?? string.Empty,
            AwsSigV4Signer.EmptyPayloadSha256Hex);

        var client = httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    internal static string BuildSafeLsnToken(string lsn, bool isInitialSnapshot)
    {
        var token = (lsn ?? "0_0").Replace('/', '_').Replace(':', '_');
        return isInitialSnapshot ? $"snap_{token}" : token;
    }
}
