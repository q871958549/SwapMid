using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SwapMid.Infrastructure;
using Volo.Abp;

namespace SwapMid.DataIntegration;

internal static partial class IcebergV2CommitWriter
{
    private static readonly string CdcIcebergSchemaCompactJson =
        BuildCdcIcebergSchemaNode().ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    internal static async Task<long?> TryReadVersionHintAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        CancellationToken cancellationToken)
    {
        var raw = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
            httpClientFactory, configuration, config, config.BuildIcebergVersionHintKey(destination), cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;
    }

    internal static Task<IcebergCommitReceipt> CommitAppendBatchAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        CdcStreamDefinition stream,
        CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument latestDoc,
        Guid runId,
        IReadOnlyList<IcebergManifestEntry> addedEntries,
        bool isInitialSnapshot,
        DateTime committedAtUtc,
        CancellationToken cancellationToken)
        => CommitWithOptimisticConcurrencyAsync(
            httpClientFactory, configuration, config, destination, stream,
            latestDoc.LatestIcebergSequenceNumber, runId,
            operation: "append",
            snapshotMode: isInitialSnapshot ? "SnapshotAppend" : "StreamingAppend",
            addedFiles: addedEntries,
            removedRelativePaths: Array.Empty<string>(),
            committedAtUtc, cancellationToken);

    internal static Task<IcebergCommitReceipt> CommitReplaceBatchAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        CdcStreamDefinition stream,
        CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument latestDoc,
        Guid compactRunId,
        IReadOnlyList<string> removedObjectKeys,
        IReadOnlyList<IcebergManifestEntry> addedEntries,
        DateTime committedAtUtc,
        CancellationToken cancellationToken)
    {
        var addedKeys = new HashSet<string>(addedEntries.Select(e => e.ObjectKey), StringComparer.Ordinal);
        var removedRelPaths = removedObjectKeys
            .Where(k => !addedKeys.Contains(k))
            .Select(k => config.ToRelativeTablePath(destination, k))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return CommitWithOptimisticConcurrencyAsync(
            httpClientFactory, configuration, config, destination, stream,
            latestDoc.LatestIcebergSequenceNumber, compactRunId,
            operation: "replace",
            snapshotMode: "CompactionReplace",
            addedFiles: addedEntries,
            removedRelativePaths: removedRelPaths,
            committedAtUtc, cancellationToken);
    }

    internal static Task<IcebergCommitReceipt> CommitMergeOnReadBatchAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        CdcStreamDefinition stream,
        CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument latestDoc,
        Guid runId,
        IReadOnlyList<IcebergManifestEntry> addedEntries,
        bool isInitialSnapshot,
        DateTime committedAtUtc,
        CancellationToken cancellationToken)
    {
        var hasDeletes = addedEntries.Any(e => e.Content == IcebergAvroManifestBuilder.ContentEqualityDeletes);
        return CommitWithOptimisticConcurrencyAsync(
            httpClientFactory, configuration, config, destination, stream,
            latestDoc.LatestIcebergSequenceNumber, runId,
            operation: hasDeletes ? "overwrite" : "append",
            snapshotMode: isInitialSnapshot ? "SnapshotUpsert" : "MergeOnReadUpsert",
            addedFiles: addedEntries,
            removedRelativePaths: Array.Empty<string>(),
            committedAtUtc, cancellationToken);
    }

    internal static async Task UploadAvroManifestArtifactsAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string manifestFileKey,
        string deleteManifestFileKey,
        string manifestListKey,
        long snapshotId,
        long? parentSnapshotId,
        long nextSeq,
        Guid runId,
        IReadOnlyList<IcebergAvroManifestBuilder.ManifestEntryRecord> avroEntries,
        int partitionSpecId,
        CancellationToken cancellationToken)
    {
        var dataEntries = avroEntries
            .Where(e => e.Content == IcebergAvroManifestBuilder.ContentData)
            .ToList();
        var deleteEntries = avroEntries
            .Where(e => e.Content == IcebergAvroManifestBuilder.ContentEqualityDeletes)
            .ToList();

        var manifestListEntries = new List<IcebergAvroManifestBuilder.ManifestListEntryRecord>(2);

        if (dataEntries.Count > 0 || deleteEntries.Count == 0)
        {
            var dataManifestBytes = IcebergAvroManifestBuilder.BuildManifestFileAvro(
                CdcIcebergSchemaCompactJson,
                dataEntries,
                syncSeed: $"mf:{snapshotId}:{runId:N}:{nextSeq}",
                partitionSpecId: partitionSpecId,
                manifestContent: IcebergAvroManifestBuilder.ManifestContentData);
            await PutBinaryObjectAsync(
                httpClientFactory, configuration, config, manifestFileKey, dataManifestBytes, "application/avro-binary", cancellationToken);
            manifestListEntries.Add(BuildManifestListEntry(
                $"s3://{config.Bucket}/{manifestFileKey}",
                dataManifestBytes.Length,
                partitionSpecId,
                IcebergAvroManifestBuilder.ManifestContentData,
                nextSeq,
                snapshotId,
                dataEntries));
        }

        if (deleteEntries.Count > 0)
        {
            var deleteManifestBytes = IcebergAvroManifestBuilder.BuildManifestFileAvro(
                CdcIcebergSchemaCompactJson,
                deleteEntries,
                syncSeed: $"dmf:{snapshotId}:{runId:N}:{nextSeq}",
                partitionSpecId: partitionSpecId,
                manifestContent: IcebergAvroManifestBuilder.ManifestContentDeletes);
            await PutBinaryObjectAsync(
                httpClientFactory, configuration, config, deleteManifestFileKey, deleteManifestBytes, "application/avro-binary", cancellationToken);
            manifestListEntries.Add(BuildManifestListEntry(
                $"s3://{config.Bucket}/{deleteManifestFileKey}",
                deleteManifestBytes.Length,
                partitionSpecId,
                IcebergAvroManifestBuilder.ManifestContentDeletes,
                nextSeq,
                snapshotId,
                deleteEntries));
        }

        var manifestListBytes = IcebergAvroManifestBuilder.BuildManifestListAvro(
            snapshotId,
            parentSnapshotId,
            nextSeq,
            manifestListEntries,
            syncSeed: $"ml:{snapshotId}:{runId:N}:{nextSeq}");
        await PutBinaryObjectAsync(
            httpClientFactory, configuration, config, manifestListKey, manifestListBytes, "application/avro-binary", cancellationToken);
    }

    private static IcebergAvroManifestBuilder.ManifestListEntryRecord BuildManifestListEntry(
        string manifestUri,
        long manifestLength,
        int partitionSpecId,
        int manifestContent,
        long nextSeq,
        long snapshotId,
        List<IcebergAvroManifestBuilder.ManifestEntryRecord> entries)
    {
        var addedCount = entries.Count(e => e.Status == IcebergAvroManifestBuilder.StatusAdded);
        var existingCount = entries.Count(e => e.Status == IcebergAvroManifestBuilder.StatusExisting);
        var deletedCount = entries.Count(e => e.Status == IcebergAvroManifestBuilder.StatusDeleted);
        var addedRows = entries.Where(e => e.Status == IcebergAvroManifestBuilder.StatusAdded).Sum(e => e.RecordCount);
        var existingRows = entries.Where(e => e.Status == IcebergAvroManifestBuilder.StatusExisting).Sum(e => e.RecordCount);
        var deletedRows = entries.Where(e => e.Status == IcebergAvroManifestBuilder.StatusDeleted).Sum(e => e.RecordCount);
        var minSeq = entries.Count > 0 ? entries.Min(e => e.FileSequenceNumber) : nextSeq;

        int? minDay = null;
        int? maxDay = null;
        var containsNull = false;
        if (partitionSpecId == IcebergAvroManifestBuilder.DailyPartitionSpecId)
        {
            var nonDeleted = entries.Where(e => e.Status != IcebergAvroManifestBuilder.StatusDeleted).ToList();
            var days = nonDeleted.Where(e => e.PartitionDayEpoch.HasValue).Select(e => e.PartitionDayEpoch!.Value).ToList();
            containsNull = nonDeleted.Any(e => !e.PartitionDayEpoch.HasValue);
            if (days.Count > 0)
            {
                minDay = days.Min();
                maxDay = days.Max();
            }
        }

        return new IcebergAvroManifestBuilder.ManifestListEntryRecord(
            ManifestPath: manifestUri,
            ManifestLength: manifestLength,
            PartitionSpecId: partitionSpecId,
            Content: manifestContent,
            SequenceNumber: nextSeq,
            MinSequenceNumber: minSeq,
            AddedSnapshotId: snapshotId,
            AddedFilesCount: addedCount,
            ExistingFilesCount: existingCount,
            DeletedFilesCount: deletedCount,
            AddedRowsCount: addedRows,
            ExistingRowsCount: existingRows,
            DeletedRowsCount: deletedRows,
            MinPartitionDayEpoch: minDay,
            MaxPartitionDayEpoch: maxDay,
            PartitionContainsNull: containsNull);
    }

    internal static async Task<List<IcebergManifestEntry>> LoadSnapshotActiveFilesAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        long snapshotId,
        string? manifestListUri,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(manifestListUri) &&
            manifestListUri.EndsWith(".avro", StringComparison.OrdinalIgnoreCase))
        {
            var listKey = IcebergAvroManifestBuilder.StripS3BucketPrefix(manifestListUri, config.Bucket);
            var listBytes = await TryGetObjectBytesAsync(httpClientFactory, configuration, config, listKey, cancellationToken);
            if (listBytes is { Length: > 0 })
            {
                try
                {
                    var manifests = IcebergAvroManifestBuilder.ReadManifestListAvro(listBytes);
                    var active = new Dictionary<string, IcebergManifestEntry>(StringComparer.Ordinal);
                    foreach (var manifest in manifests)
                    {
                        var mfKey = IcebergAvroManifestBuilder.StripS3BucketPrefix(manifest.ManifestPath, config.Bucket);
                        var mfBytes = await TryGetObjectBytesAsync(httpClientFactory, configuration, config, mfKey, cancellationToken);
                        if (mfBytes is not { Length: > 0 }) continue;

                        foreach (var entry in IcebergAvroManifestBuilder.ReadManifestFileAvro(mfBytes))
                        {
                            var objectKey = IcebergAvroManifestBuilder.StripS3BucketPrefix(entry.FilePath, config.Bucket);
                            var relPath = config.ToRelativeTablePath(destination, objectKey);
                            if (entry.Status == IcebergAvroManifestBuilder.StatusDeleted)
                            {
                                active.Remove(relPath);
                                continue;
                            }

                            active[relPath] = new IcebergManifestEntry
                            {
                                RelativePath = relPath,
                                ObjectKey = objectKey,
                                FilePath = entry.FilePath,
                                FileFormat = entry.FileFormat,
                                FileSizeInBytes = entry.FileSizeInBytes,
                                RecordCount = entry.RecordCount,
                                AddedAtSequence = entry.FileSequenceNumber > 0 ? entry.FileSequenceNumber : entry.SequenceNumber,
                                PartitionDayEpoch = entry.PartitionDayEpoch ?? IcebergAvroManifestBuilder.TryExtractUtcDayFromPath(relPath),
                                Content = entry.Content,
                                EqualityIds = entry.EqualityIds,
                            };
                        }
                    }

                    return active.Values
                        .OrderBy(f => f.AddedAtSequence)
                        .ThenBy(f => f.RelativePath, StringComparer.Ordinal)
                        .ToList();
                }
                catch (InvalidDataException)
                {
                    // Fall through to legacy JSON manifest if Avro payload is malformed.
                }
                catch (EndOfStreamException)
                {
                    // Fall through to legacy JSON manifest if Avro payload is truncated.
                }
            }
        }

        // Backward-compatible fallback for v2.3.0 JSON snapshot manifests.
        var jsonKey = !string.IsNullOrWhiteSpace(manifestListUri) && manifestListUri.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? IcebergAvroManifestBuilder.StripS3BucketPrefix(manifestListUri, config.Bucket)
            : config.BuildIcebergManifestKey(destination, snapshotId);
        var manifestJson = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
            httpClientFactory, configuration, config, jsonKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return new List<IcebergManifestEntry>();
        }

        try
        {
            var doc = JsonSerializer.Deserialize<IcebergSnapshotManifestDocument>(manifestJson, JsonOptions);
            return doc?.ActiveFiles ?? new List<IcebergManifestEntry>();
        }
        catch (JsonException)
        {
            return new List<IcebergManifestEntry>();
        }
    }

    private static async Task PutBinaryObjectAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string objectKey,
        byte[] bytes,
        string mediaType,
        CancellationToken cancellationToken)
    {
        var objectUri = config.BuildObjectUri(objectKey);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(objectUri, configuration, cancellationToken);

        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
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
                .WithData("Reason", SinkConfigContractHelpers.RedactDiagnosticText($"Apache Iceberg Avro PUT 失败 ({(int)response.StatusCode}): {err}"));
        }
    }

    private static async Task<byte[]?> TryGetObjectBytesAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string objectKey,
        CancellationToken cancellationToken)
    {
        var objectUri = config.BuildObjectUri(objectKey);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(objectUri, configuration, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, objectUri);
        AwsSigV4Signer.Sign(
            request, config.Region, config.AccessKey, config.SecretKey ?? string.Empty, AwsSigV4Signer.EmptyPayloadSha256Hex);

        var client = httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
