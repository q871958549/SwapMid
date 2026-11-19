using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NpgsqlTypes;
using SwapMid.Monitoring;

namespace SwapMid.DataIntegration;

internal static partial class CdcObjectStorageLakehouseWriter
{
    private static async Task<long> DeliverMergeOnReadIcebergAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        ObjectStorageSinkConfig config,
        string destination,
        IReadOnlyList<PgOutputCapturedChange> changes,
        NpgsqlLogSequenceNumber? batchEndLsn,
        ObjectStorageEtlSinkWriter writer,
        string[] columns,
        Type[] clrTypes,
        List<object?[]> rows,
        string?[]? partitionKeys,
        Guid runId,
        string endLsnText,
        string? idempotencyKeyOverride,
        CancellationToken cancellationToken,
        int[]? fieldIds = null)
    {
        var committedAtUtc = DateTime.UtcNow;
        var isInitialSnapshot = IsInitialSnapshotOverride(idempotencyKeyOverride);
        var lsnToken = BuildSafeLsnToken(batchEndLsn?.ToString() ?? changes[^1].Lsn, isInitialSnapshot);
        var latestKey = config.BuildLatestManifestKey(destination);
        var latestDoc = await LoadOrInitializeLatestAsync(
            writer.HttpClientFactory, writer.Configuration, config, latestKey, stream, destination, "parquet", cancellationToken);
        var alreadyCommitted = latestDoc.RecentFiles.Any(f => f.RunId == runId);
        var hasPriorSnapshot = (latestDoc.LatestIcebergSequenceNumber ?? 0L) > 0L;
        var isDaily = stream.LakehousePartitionStrategy == LakehousePartitionStrategy.DailyCommitUtc;

        var buckets = BuildMergeOnReadBuckets(changes, rows, partitionKeys, isDaily, hasPriorSnapshot);
        var icebergEntries = new List<IcebergV2CommitWriter.IcebergManifestEntry>(buckets.Count * 2);
        long totalDeliveredEvents = 0;
        long totalDeleteKeyCount = 0;
        var lastManifestKey = latestDoc.LatestManifestKey;
        var lastObjectKey = latestDoc.LatestDataObjectKey;

        foreach (var bucket in buckets)
        {
            var bucketRunId = buckets.Count == 1
                ? runId
                : CdcProtocolHelpers.BuildDeterministicPipelineRunId(
                    stream.Id, $"{endLsnText}:mor:{bucket.PartitionDayEpoch?.ToString() ?? "all"}");
            var segment = bucket.PartitionDayEpoch.HasValue
                ? IcebergAvroManifestBuilder.FormatUtcDayPartitionSegment(bucket.PartitionDayEpoch.Value)
                : null;

            if (bucket.LiveRows.Count > 0)
            {
                var uploaded = await writer.WriteAndUploadCdcBatchAsync(
                    bucketRunId, targetSink, destination,
                    async (consumeBatchAsync, ct) =>
                        await consumeBatchAsync(
                            new EtlSourceBatch(columns, clrTypes, bucket.LiveRows, 0, bucket.LivePartitionKeys), ct),
                    cancellationToken,
                    partitionSegment: segment,
                    fieldIds: fieldIds);
                if (uploaded.RowCount > 0)
                {
                    var suffix = bucket.PartitionDayEpoch.HasValue ? $"{lsnToken}_d{bucket.PartitionDayEpoch.Value}" : lsnToken;
                    var manifestKey = config.BuildBatchManifestKey(destination, suffix, bucketRunId);
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
                        SchemaBaselineHash = stream.SchemaBaselineHash,
                        MonitoredTables = stream.MonitoredTables,
                        CommittedAtUtc = committedAtUtc,
                    };
                    await PutJsonObjectAsync(
                        writer.HttpClientFactory, writer.Configuration, config, manifestKey,
                        JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);

                    lastManifestKey = manifestKey;
                    lastObjectKey = uploaded.ObjectKey;
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
                        PartitionDayEpoch = bucket.PartitionDayEpoch,
                        CommittedAtUtc = committedAtUtc,
                    });

                    icebergEntries.Add(new IcebergV2CommitWriter.IcebergManifestEntry
                    {
                        RelativePath = config.ToRelativeTablePath(destination, uploaded.ObjectKey),
                        ObjectKey = uploaded.ObjectKey,
                        FilePath = $"s3://{config.Bucket}/{uploaded.ObjectKey}",
                        FileFormat = "PARQUET",
                        FileSizeInBytes = uploaded.ByteSize,
                        RecordCount = uploaded.RowCount,
                        PartitionDayEpoch = bucket.PartitionDayEpoch,
                        Content = IcebergAvroManifestBuilder.ContentData,
                    });
                }
            }

            if (bucket.DeleteKeys.Count > 0)
            {
                var eqDel = await writer.WriteAndUploadEqualityDeleteParquetAsync(
                    bucketRunId, targetSink, destination, bucket.DeleteKeys, cancellationToken, partitionSegment: segment);
                if (eqDel.RowCount > 0)
                {
                    totalDeleteKeyCount += eqDel.RowCount;
                    icebergEntries.Add(new IcebergV2CommitWriter.IcebergManifestEntry
                    {
                        RelativePath = config.ToRelativeTablePath(destination, eqDel.ObjectKey),
                        ObjectKey = eqDel.ObjectKey,
                        FilePath = $"s3://{config.Bucket}/{eqDel.ObjectKey}",
                        FileFormat = "PARQUET",
                        FileSizeInBytes = eqDel.ByteSize,
                        RecordCount = eqDel.RowCount,
                        PartitionDayEpoch = bucket.PartitionDayEpoch,
                        Content = IcebergAvroManifestBuilder.ContentEqualityDeletes,
                        EqualityIds = [IcebergAvroManifestBuilder.BusinessKeyFieldId],
                    });
                }
            }

            totalDeliveredEvents += bucket.SourceEventCount;
        }

        if (icebergEntries.Count == 0)
        {
            return 0;
        }

        latestDoc.StreamName = stream.Name;
        latestDoc.Format = "parquet";
        latestDoc.CheckpointLsn = batchEndLsn?.ToString() ?? changes[^1].Lsn;
        latestDoc.SchemaBaselineHash = stream.SchemaBaselineHash ?? latestDoc.SchemaBaselineHash;
        latestDoc.LatestManifestKey = lastManifestKey;
        latestDoc.LatestDataObjectKey = lastObjectKey;
        latestDoc.UpdatedAtUtc = committedAtUtc;
        if (latestDoc.RecentFiles.Count > MaxRecentFilesInLatest)
        {
            latestDoc.RecentFiles = latestDoc.RecentFiles.Skip(latestDoc.RecentFiles.Count - MaxRecentFilesInLatest).ToList();
        }

        if (!alreadyCommitted)
        {
            var receipt = await IcebergV2CommitWriter.CommitMergeOnReadBatchAsync(
                writer.HttpClientFactory, writer.Configuration, config, destination, stream, latestDoc,
                runId, icebergEntries, isInitialSnapshot, committedAtUtc, cancellationToken);
            latestDoc.LatestIcebergSequenceNumber = receipt.SequenceNumber;
            latestDoc.LatestIcebergSnapshotId = receipt.SnapshotId;
            latestDoc.LatestIcebergMetadataLocation = receipt.MetadataLocationKey;
            if (totalDeleteKeyCount > 0)
            {
                PlatformMetrics.CdcIcebergEqualityDeletesTotal.Inc(totalDeleteKeyCount);
            }
        }

        await PutJsonObjectAsync(
            writer.HttpClientFactory, writer.Configuration, config, latestKey,
            JsonSerializer.Serialize(latestDoc, JsonOptions), cancellationToken);
        PlatformMetrics.CdcObjectStorageManifestsTotal.WithLabels("parquet").Inc();
        return totalDeliveredEvents;
    }

    private sealed record MergeOnReadBucket(
        int? PartitionDayEpoch,
        List<object?[]> LiveRows,
        string?[] LivePartitionKeys,
        List<string> DeleteKeys,
        int SourceEventCount);

    private static List<MergeOnReadBucket> BuildMergeOnReadBuckets(
        IReadOnlyList<PgOutputCapturedChange> changes,
        List<object?[]> rows,
        string?[]? partitionKeys,
        bool isDaily,
        bool hasPriorSnapshot)
    {
        var groupedIndices = new SortedDictionary<int, List<int>>();
        for (var i = 0; i < changes.Count; i++)
        {
            var bucketKey = isDaily
                ? IcebergAvroManifestBuilder.ToUtcEpochDay(changes[i].CommitTimestampUtc)
                : int.MinValue;
            if (!groupedIndices.TryGetValue(bucketKey, out var list))
            {
                list = new List<int>();
                groupedIndices[bucketKey] = list;
            }

            list.Add(i);
        }

        var result = new List<MergeOnReadBucket>(groupedIndices.Count);
        foreach (var (bucketKey, indices) in groupedIndices)
        {
            var finalIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var keylessLiveIndices = new List<int>();
            var deleteKeySet = new HashSet<string>(StringComparer.Ordinal);
            var deleteKeyOrder = new List<string>();

            foreach (var idx in indices)
            {
                var key = partitionKeys != null && idx < partitionKeys.Length ? partitionKeys[idx] : null;
                var op = changes[idx].Operation;
                var isDelete = string.Equals(op, "delete", StringComparison.OrdinalIgnoreCase);
                var isUpdate = string.Equals(op, "update", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(key))
                {
                    if (!isDelete) keylessLiveIndices.Add(idx);
                    continue;
                }

                if ((hasPriorSnapshot || isUpdate || isDelete) && deleteKeySet.Add(key))
                {
                    deleteKeyOrder.Add(key);
                }

                if (isDelete)
                {
                    finalIndexByKey.Remove(key);
                }
                else
                {
                    finalIndexByKey[key] = idx;
                }
            }

            var liveIndices = finalIndexByKey.Values
                .Concat(keylessLiveIndices)
                .OrderBy(i => i)
                .ToList();
            var liveRows = liveIndices.Select(i => rows[i]).ToList();
            var liveKeys = liveIndices.Select(i => partitionKeys != null && i < partitionKeys.Length ? partitionKeys[i] : null).ToArray();

            result.Add(new MergeOnReadBucket(
                isDaily ? bucketKey : null,
                liveRows,
                liveKeys,
                deleteKeyOrder,
                indices.Count));
        }

        return result;
    }
}
