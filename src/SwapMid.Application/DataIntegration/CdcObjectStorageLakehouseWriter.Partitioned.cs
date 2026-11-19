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
    private static async Task<long> DeliverDailyPartitionedIcebergAsync(
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
        CancellationToken cancellationToken)
    {
        var dayBuckets = new SortedDictionary<int, List<int>>();
        for (var i = 0; i < changes.Count; i++)
        {
            var epochDay = IcebergAvroManifestBuilder.ToUtcEpochDay(changes[i].CommitTimestampUtc);
            if (!dayBuckets.TryGetValue(epochDay, out var indices))
            {
                indices = new List<int>();
                dayBuckets[epochDay] = indices;
            }

            indices.Add(i);
        }

        var committedAtUtc = DateTime.UtcNow;
        var isInitialSnapshot = IsInitialSnapshotOverride(idempotencyKeyOverride);
        var lsnToken = BuildSafeLsnToken(batchEndLsn?.ToString() ?? changes[^1].Lsn, isInitialSnapshot);
        var latestKey = config.BuildLatestManifestKey(destination);
        var latestDoc = await LoadOrInitializeLatestAsync(
            writer.HttpClientFactory, writer.Configuration, config, latestKey, stream, destination, "parquet", cancellationToken);
        var alreadyCommitted = latestDoc.RecentFiles.Any(f => f.RunId == runId);

        var icebergEntries = new List<IcebergV2CommitWriter.IcebergManifestEntry>(dayBuckets.Count);
        long totalRows = 0;
        var lastManifestKey = latestDoc.LatestManifestKey;
        var lastObjectKey = latestDoc.LatestDataObjectKey;

        foreach (var (epochDay, indices) in dayBuckets)
        {
            var partRows = indices.Select(idx => rows[idx]).ToList();
            var partKeys = partitionKeys != null ? indices.Select(idx => partitionKeys[idx]).ToArray() : null;
            var dayRunId = dayBuckets.Count == 1
                ? runId
                : CdcProtocolHelpers.BuildDeterministicPipelineRunId(stream.Id, $"{endLsnText}:day:{epochDay}");
            var segment = IcebergAvroManifestBuilder.FormatUtcDayPartitionSegment(epochDay);

            var uploaded = await writer.WriteAndUploadCdcBatchAsync(
                dayRunId, targetSink, destination,
                async (consumeBatchAsync, ct) =>
                    await consumeBatchAsync(new EtlSourceBatch(columns, clrTypes, partRows, 0, partKeys), ct),
                cancellationToken,
                partitionSegment: segment);
            if (uploaded.RowCount == 0) continue;

            totalRows += uploaded.RowCount;
            var manifestKey = config.BuildBatchManifestKey(destination, $"{lsnToken}_d{epochDay}", dayRunId);
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
                StartLsn = changes[indices[0]].Lsn,
                EndLsn = batchEndLsn?.ToString() ?? changes[indices[^1]].Lsn,
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
                PartitionDayEpoch = epochDay,
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
                PartitionDayEpoch = epochDay,
            });
        }

        if (totalRows == 0) return 0;

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

        if (!alreadyCommitted && icebergEntries.Count > 0)
        {
            var receipt = await IcebergV2CommitWriter.CommitAppendBatchAsync(
                writer.HttpClientFactory, writer.Configuration, config, destination, stream, latestDoc,
                runId, icebergEntries, isInitialSnapshot, committedAtUtc, cancellationToken);
            latestDoc.LatestIcebergSequenceNumber = receipt.SequenceNumber;
            latestDoc.LatestIcebergSnapshotId = receipt.SnapshotId;
            latestDoc.LatestIcebergMetadataLocation = receipt.MetadataLocationKey;
        }

        await PutJsonObjectAsync(
            writer.HttpClientFactory, writer.Configuration, config, latestKey,
            JsonSerializer.Serialize(latestDoc, JsonOptions), cancellationToken);
        PlatformMetrics.CdcObjectStorageManifestsTotal.WithLabels("parquet").Inc();
        return totalRows;
    }
}
