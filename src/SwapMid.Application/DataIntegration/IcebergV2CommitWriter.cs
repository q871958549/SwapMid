using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SwapMid.Infrastructure;
using SwapMid.Monitoring;
using Volo.Abp;

namespace SwapMid.DataIntegration;

internal readonly record struct IcebergCommitReceipt(
    long SequenceNumber,
    long SnapshotId,
    string MetadataLocationKey,
    string ManifestLocationKey);

/// <summary>
/// 原生 Apache Iceberg v2 表元数据写入器（纯 .NET，零 JVM 依赖）：
/// 为 Parquet CDC 分片与压缩合并原子提交符合 Iceberg v2 规范的
/// <c>metadata/v{seq}.metadata.json</c>、<c>metadata/snap-{snapshotId}-manifest.json</c> 与
/// <c>metadata/version-hint.text</c>，并基于 S3 <c>If-None-Match: *</c> 实现 CAS 乐观并发与 RunId 幂等恢复。
/// </summary>
internal static partial class IcebergV2CommitWriter
{
    private const int MaxOptimisticRetries = 64;
    private const string EngineName = "SwapMid-2.6.0";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal sealed class IcebergManifestEntry
    {
        [JsonPropertyName("relative-path")]
        public string RelativePath { get; set; } = string.Empty;

        [JsonPropertyName("object-key")]
        public string ObjectKey { get; set; } = string.Empty;

        [JsonPropertyName("file-path")]
        public string FilePath { get; set; } = string.Empty;

        [JsonPropertyName("file-format")]
        public string FileFormat { get; set; } = "PARQUET";

        [JsonPropertyName("file-size-in-bytes")]
        public long FileSizeInBytes { get; set; }

        [JsonPropertyName("record-count")]
        public long RecordCount { get; set; }

        [JsonPropertyName("added-at-sequence")]
        public long AddedAtSequence { get; set; }

        [JsonPropertyName("partition-day-epoch")]
        public int? PartitionDayEpoch { get; set; }

        [JsonPropertyName("content")]
        public int Content { get; set; } = IcebergAvroManifestBuilder.ContentData;

        [JsonPropertyName("equality-ids")]
        public int[]? EqualityIds { get; set; }
    }

    internal sealed class IcebergSnapshotManifestDocument
    {
        [JsonPropertyName("format-version")]
        public int FormatVersion { get; set; } = 2;

        [JsonPropertyName("sequence-number")]
        public long SequenceNumber { get; set; }

        [JsonPropertyName("snapshot-id")]
        public long SnapshotId { get; set; }

        [JsonPropertyName("operation")]
        public string Operation { get; set; } = "append";

        [JsonPropertyName("swapmid-run-id")]
        public string SwapMidRunId { get; set; } = string.Empty;

        [JsonPropertyName("timestamp-ms")]
        public long TimestampMs { get; set; }

        [JsonPropertyName("active-files")]
        public List<IcebergManifestEntry> ActiveFiles { get; set; } = new();

        [JsonPropertyName("added-files")]
        public List<IcebergManifestEntry> AddedFiles { get; set; } = new();

        [JsonPropertyName("deleted-files")]
        public List<string> DeletedFiles { get; set; } = new();
    }

    internal static Task<IcebergCommitReceipt> CommitAppendAsync(
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
        var addedEntry = new IcebergManifestEntry
        {
            RelativePath = config.ToRelativeTablePath(destination, dataObjectKey),
            ObjectKey = dataObjectKey,
            FilePath = $"s3://{config.Bucket}/{dataObjectKey}",
            FileFormat = "PARQUET",
            FileSizeInBytes = byteSize,
            RecordCount = rowCount,
        };
        return CommitWithOptimisticConcurrencyAsync(
            httpClientFactory, configuration, config, destination, stream,
            latestDoc.LatestIcebergSequenceNumber, runId,
            operation: "append",
            snapshotMode: isInitialSnapshot ? "SnapshotAppend" : "StreamingAppend",
            addedFiles: [addedEntry],
            removedRelativePaths: Array.Empty<string>(),
            committedAtUtc, cancellationToken);
    }

    internal static Task<IcebergCommitReceipt> CommitReplaceCompactionAsync(
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
        var removedRelPaths = removedObjectKeys
            .Where(k => !string.Equals(k, addedObjectKey, StringComparison.Ordinal))
            .Select(k => config.ToRelativeTablePath(destination, k))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var addedEntry = new IcebergManifestEntry
        {
            RelativePath = config.ToRelativeTablePath(destination, addedObjectKey),
            ObjectKey = addedObjectKey,
            FilePath = $"s3://{config.Bucket}/{addedObjectKey}",
            FileFormat = "PARQUET",
            FileSizeInBytes = outputByteSize,
            RecordCount = totalRowCount,
        };
        return CommitWithOptimisticConcurrencyAsync(
            httpClientFactory, configuration, config, destination, stream,
            latestDoc.LatestIcebergSequenceNumber, compactRunId,
            operation: "replace",
            snapshotMode: "CompactionReplace",
            addedFiles: [addedEntry],
            removedRelativePaths: removedRelPaths,
            committedAtUtc, cancellationToken);
    }

    private static async Task<IcebergCommitReceipt> CommitWithOptimisticConcurrencyAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        CdcStreamDefinition stream,
        long? knownSequenceHint,
        Guid runId,
        string operation,
        string snapshotMode,
        IReadOnlyList<IcebergManifestEntry> addedFiles,
        IReadOnlyList<string> removedRelativePaths,
        DateTime committedAtUtc,
        CancellationToken cancellationToken)
    {
        var runToken = runId.ToString("D");
        var tsMs = new DateTimeOffset(DateTime.SpecifyKind(committedAtUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var tableDir = config.BuildDestinationDirectory(destination).TrimEnd('/');
        var tableLocation = string.IsNullOrEmpty(tableDir)
            ? $"s3://{config.Bucket}"
            : $"s3://{config.Bucket}/{tableDir}";

        var hintSeq = await TryReadVersionHintAsync(httpClientFactory, configuration, config, destination, cancellationToken);
        var baseSeq = Math.Max(hintSeq ?? 0L, knownSequenceHint ?? 0L);

        for (var attempt = 0; attempt < MaxOptimisticRetries; attempt++)
        {
            var (latestSeq, latestMetadataJson) = await ProbeLatestMetadataAsync(
                httpClientFactory, configuration, config, destination, baseSeq, cancellationToken);

            if (!string.IsNullOrWhiteSpace(latestMetadataJson) &&
                TryFindExistingReceiptForRunId(latestMetadataJson, config, destination, runToken, out var replayReceipt))
            {
                await PutVersionHintAsync(httpClientFactory, configuration, config, destination, latestSeq, cancellationToken);
                return replayReceipt;
            }

            var nextSeq = latestSeq + 1;
            var snapshotId = ComputeDeterministicSnapshotId(stream.Id, runId, nextSeq);
            var metadataKey = config.BuildIcebergMetadataKey(destination, nextSeq);
            var manifestListKey = config.BuildIcebergAvroManifestListKey(destination, snapshotId, runId);
            var manifestFileKey = config.BuildIcebergAvroManifestFileKey(destination, nextSeq, runId);
            var deleteManifestFileKey = config.BuildIcebergAvroDeleteManifestFileKey(destination, nextSeq, runId);

            long? parentSnapshotId = !string.IsNullOrWhiteSpace(latestMetadataJson) &&
                                     TryExtractCurrentSnapshotId(latestMetadataJson, out var pSnap)
                ? pSnap
                : null;

            var prevActiveFiles = await LoadPreviousActiveFilesAsync(
                httpClientFactory, configuration, config, destination, latestMetadataJson, cancellationToken);
            var partitionSpecId = stream.LakehousePartitionStrategy == LakehousePartitionStrategy.DailyCommitUtc
                ? IcebergAvroManifestBuilder.DailyPartitionSpecId
                : IcebergAvroManifestBuilder.UnpartitionedSpecId;
            var (activeFiles, avroEntries) = BuildNextSnapshotFiles(
                prevActiveFiles, nextSeq, snapshotId, addedFiles, removedRelativePaths);

            await UploadAvroManifestArtifactsAsync(
                httpClientFactory, configuration, config,
                manifestFileKey, deleteManifestFileKey, manifestListKey, snapshotId, parentSnapshotId, nextSeq, runId,
                avroEntries, partitionSpecId, cancellationToken);

            var metadataJson = BuildTableMetadataJson(
                stream, tableLocation, config.Bucket,
                latestMetadataJson,
                latestSeq > 0 ? config.BuildIcebergMetadataKey(destination, latestSeq) : null,
                nextSeq, snapshotId, manifestListKey, operation, snapshotMode, runToken, tsMs,
                addedFiles, removedRelativePaths, activeFiles);

            if (await PutMetadataIfAbsentAsync(httpClientFactory, configuration, config, metadataKey, metadataJson, cancellationToken))
            {
                await PutVersionHintAsync(httpClientFactory, configuration, config, destination, nextSeq, cancellationToken);
                PlatformMetrics.CdcIcebergCommitsTotal.WithLabels(operation, "succeeded").Inc();
                return new IcebergCommitReceipt(nextSeq, snapshotId, metadataKey, manifestListKey);
            }

            PlatformMetrics.CdcIcebergCommitsTotal.WithLabels(operation, "retried").Inc();
            var racedJson = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
                httpClientFactory, configuration, config, metadataKey, cancellationToken);
            if (!string.IsNullOrWhiteSpace(racedJson) &&
                TryFindExistingReceiptForRunId(racedJson, config, destination, runToken, out var racedReplay))
            {
                await PutVersionHintAsync(httpClientFactory, configuration, config, destination, nextSeq, cancellationToken);
                return racedReplay;
            }

            baseSeq = nextSeq;
        }

        PlatformMetrics.CdcIcebergCommitsTotal.WithLabels(operation, "failed").Inc();
        throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
            .WithData("Reason", "Apache Iceberg v2 元数据并发提交版本号竞争超出最大重试次数，请重试提交。");
    }

    private static async Task<(long LatestSeq, string? LatestJson)> ProbeLatestMetadataAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        long startSeq,
        CancellationToken cancellationToken)
    {
        long latestSeq = 0;
        string? latestJson = null;

        if (startSeq > 0)
        {
            var initial = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
                httpClientFactory, configuration, config, config.BuildIcebergMetadataKey(destination, startSeq), cancellationToken);
            if (!string.IsNullOrWhiteSpace(initial))
            {
                latestSeq = startSeq;
                latestJson = initial;
            }
        }

        for (var probe = latestSeq + 1; probe <= latestSeq + MaxOptimisticRetries; probe++)
        {
            var next = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
                httpClientFactory, configuration, config, config.BuildIcebergMetadataKey(destination, probe), cancellationToken);
            if (string.IsNullOrWhiteSpace(next))
            {
                break;
            }

            latestSeq = probe;
            latestJson = next;
        }

        return (latestSeq, latestJson);
    }

    private static async Task<List<IcebergManifestEntry>> LoadPreviousActiveFilesAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        string? latestMetadataJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(latestMetadataJson) ||
            !TryExtractCurrentSnapshotId(latestMetadataJson, out var currentSnapId))
        {
            return new List<IcebergManifestEntry>();
        }

        TryExtractSnapshotManifestListUri(latestMetadataJson, currentSnapId, out var manifestListUri);
        return await LoadSnapshotActiveFilesAsync(
            httpClientFactory, configuration, config, destination, currentSnapId, manifestListUri, cancellationToken);
    }

    private static (List<IcebergManifestEntry> ActiveFiles, List<IcebergAvroManifestBuilder.ManifestEntryRecord> AvroEntries)
        BuildNextSnapshotFiles(
            IReadOnlyList<IcebergManifestEntry> previousActiveFiles,
            long nextSeq,
            long snapshotId,
            IReadOnlyList<IcebergManifestEntry> addedFiles,
            IReadOnlyList<string> removedRelativePaths)
    {
        var removedSet = new HashSet<string>(removedRelativePaths, StringComparer.Ordinal);
        var addedSet = new HashSet<string>(addedFiles.Select(f => f.RelativePath), StringComparer.Ordinal);
        var activeMap = new Dictionary<string, IcebergManifestEntry>(StringComparer.Ordinal);
        var avroEntries = new List<IcebergAvroManifestBuilder.ManifestEntryRecord>(previousActiveFiles.Count + addedFiles.Count);

        foreach (var prev in previousActiveFiles)
        {
            var prevDay = prev.PartitionDayEpoch ?? IcebergAvroManifestBuilder.TryExtractUtcDayFromPath(prev.RelativePath);
            if (removedSet.Contains(prev.RelativePath))
            {
                avroEntries.Add(new IcebergAvroManifestBuilder.ManifestEntryRecord(
                    IcebergAvroManifestBuilder.StatusDeleted,
                    snapshotId,
                    nextSeq,
                    prev.AddedAtSequence > 0 ? prev.AddedAtSequence : nextSeq,
                    prev.FilePath,
                    prev.FileFormat,
                    prev.RecordCount,
                    prev.FileSizeInBytes,
                    prevDay,
                    prev.Content,
                    prev.EqualityIds));
                continue;
            }

            if (!addedSet.Contains(prev.RelativePath))
            {
                activeMap[prev.RelativePath] = prev;
                avroEntries.Add(new IcebergAvroManifestBuilder.ManifestEntryRecord(
                    IcebergAvroManifestBuilder.StatusExisting,
                    snapshotId,
                    prev.AddedAtSequence > 0 ? prev.AddedAtSequence : nextSeq,
                    prev.AddedAtSequence > 0 ? prev.AddedAtSequence : nextSeq,
                    prev.FilePath,
                    prev.FileFormat,
                    prev.RecordCount,
                    prev.FileSizeInBytes,
                    prevDay,
                    prev.Content,
                    prev.EqualityIds));
            }
        }

        foreach (var added in addedFiles)
        {
            var addedDay = added.PartitionDayEpoch ?? IcebergAvroManifestBuilder.TryExtractUtcDayFromPath(added.RelativePath);
            var entry = new IcebergManifestEntry
            {
                RelativePath = added.RelativePath,
                ObjectKey = added.ObjectKey,
                FilePath = added.FilePath,
                FileFormat = added.FileFormat,
                FileSizeInBytes = added.FileSizeInBytes,
                RecordCount = added.RecordCount,
                AddedAtSequence = nextSeq,
                PartitionDayEpoch = addedDay,
                Content = added.Content,
                EqualityIds = added.EqualityIds,
            };
            activeMap[added.RelativePath] = entry;
            avroEntries.Add(new IcebergAvroManifestBuilder.ManifestEntryRecord(
                IcebergAvroManifestBuilder.StatusAdded,
                snapshotId,
                nextSeq,
                nextSeq,
                entry.FilePath,
                entry.FileFormat,
                entry.RecordCount,
                entry.FileSizeInBytes,
                addedDay,
                entry.Content,
                entry.EqualityIds));
        }

        var activeList = activeMap.Values
            .OrderBy(f => f.AddedAtSequence)
            .ThenBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
        return (activeList, avroEntries);
    }

    internal static long ComputeDeterministicSnapshotId(Guid streamId, Guid runId, long sequenceNumber)
    {
        var seed = $"{streamId:D}:{runId:D}:{sequenceNumber.ToString(CultureInfo.InvariantCulture)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var raw = BitConverter.ToInt64(hash, 0) & long.MaxValue;
        return raw == 0 ? sequenceNumber : raw;
    }

    private static async Task<bool> PutMetadataIfAbsentAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string metadataKey,
        string metadataJson,
        CancellationToken cancellationToken)
    {
        var objectUri = config.BuildObjectUri(metadataKey);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(objectUri, configuration, cancellationToken);

        var bytes = Encoding.UTF8.GetBytes(metadataJson);
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentLength = bytes.Length;

        using var request = new HttpRequestMessage(HttpMethod.Put, objectUri) { Content = content };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        AwsSigV4Signer.Sign(
            request, config.Region, config.AccessKey, config.SecretKey ?? string.Empty, AwsSigV4Signer.ComputeSha256Hex(bytes));

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
            .WithData("Reason", SinkConfigContractHelpers.RedactDiagnosticText($"Apache Iceberg v2 元数据 PUT 失败 ({(int)response.StatusCode}): {err}"));
    }

    private static async Task PutVersionHintAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        long sequenceNumber,
        CancellationToken cancellationToken)
    {
        var objectUri = config.BuildObjectUri(config.BuildIcebergVersionHintKey(destination));
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(objectUri, configuration, cancellationToken);

        var bytes = Encoding.UTF8.GetBytes(sequenceNumber.ToString(CultureInfo.InvariantCulture));
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Headers.ContentLength = bytes.Length;

        using var request = new HttpRequestMessage(HttpMethod.Put, objectUri) { Content = content };
        AwsSigV4Signer.Sign(
            request, config.Region, config.AccessKey, config.SecretKey ?? string.Empty, AwsSigV4Signer.ComputeSha256Hex(bytes));

        var client = httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                .WithData("Reason", SinkConfigContractHelpers.RedactDiagnosticText($"Apache Iceberg version-hint.text PUT 失败 ({(int)response.StatusCode}): {err}"));
        }
    }
}
