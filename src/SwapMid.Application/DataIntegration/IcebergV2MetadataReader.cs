using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// 读取 S3/MinIO Apache Iceberg v2 元数据（<c>metadata/version-hint.text</c> + <c>metadata/v{seq}.metadata.json</c> +
/// <c>metadata/snap-{snapshotId}-manifest.json</c>）并回放任意快照版本的 Time-Travel 活跃 Parquet 分片视图。
/// </summary>
internal static class IcebergV2MetadataReader
{
    internal static async Task<CdcLakehouseHistoryDto> ReadAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        CdcStreamDefinition stream,
        ObjectStorageSinkConfig config,
        string destination,
        CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument? latestDoc,
        long? requestedVersion,
        int boundedMax,
        CancellationToken cancellationToken)
    {
        var metadataDir = config.BuildIcebergMetadataDirectory(destination);
        var tableDir = config.BuildDestinationDirectory(destination).TrimEnd('/');

        var hintSeq = await IcebergV2CommitWriter.TryReadVersionHintAsync(
            httpClientFactory,
            configuration,
            config,
            destination,
            cancellationToken);
        var latestSeq = hintSeq ?? latestDoc?.LatestIcebergSequenceNumber;

        string? metadataJson = null;
        if (latestSeq is > 0)
        {
            metadataJson = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
                httpClientFactory,
                configuration,
                config,
                config.BuildIcebergMetadataKey(destination, latestSeq.Value),
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            if (requestedVersion.HasValue)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"目标表尚未生成 Apache Iceberg v2 元数据提交，版本 v{requestedVersion.Value} 不存在。");
            }

            return new CdcLakehouseHistoryDto
            {
                StreamId = stream.Id,
                Destination = destination,
                TableFormat = LakehouseTableFormat.ApacheIceberg,
                DeltaLogDirectory = metadataDir,
                CurrentVersion = null,
                SelectedVersion = null,
                TotalCommitCount = 0,
                Summary = "目标表尚未产生 Apache Iceberg v2 快照，请先执行快照、捕获或 Webhook 推送。",
            };
        }

        var snapshots = ParseSnapshots(metadataJson, config, destination);
        if (snapshots.Count == 0)
        {
            return new CdcLakehouseHistoryDto
            {
                StreamId = stream.Id,
                Destination = destination,
                TableFormat = LakehouseTableFormat.ApacheIceberg,
                DeltaLogDirectory = metadataDir,
                CurrentVersion = null,
                SelectedVersion = null,
                TotalCommitCount = 0,
                Summary = "Apache Iceberg v2 元数据中暂无已提交快照。",
            };
        }

        var currentSeq = snapshots[^1].Summary.Version;
        var selectedSeq = requestedVersion ?? currentSeq;
        var selectedSnap = snapshots.FirstOrDefault(s => s.Summary.Version == selectedSeq);
        if (selectedSnap == null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"请求的 Apache Iceberg Time-Travel 版本 v{selectedSeq} 不存在（已提交范围 v{snapshots[0].Summary.Version}..v{currentSeq}）。");
        }

        var activeFiles = await ReadSnapshotActiveFilesAsync(
            httpClientFactory,
            configuration,
            config,
            destination,
            selectedSnap.SnapshotId,
            selectedSnap.ManifestListUri,
            cancellationToken);

        var totalBytes = activeFiles.Sum(f => f.SizeBytes);
        var totalRecords = activeFiles.Sum(f => f.RecordCount ?? 0);
        var recentCommits = snapshots
            .OrderByDescending(s => s.Summary.Version)
            .Take(boundedMax)
            .Select(s => s.Summary)
            .ToList();

        var timeTravelSql = BuildIcebergTimeTravelSql(
            config,
            tableDir,
            selectedSeq,
            selectedSnap.SnapshotId,
            stream.LakehousePartitionStrategy,
            activeFiles);

        var partitionTag = stream.LakehousePartitionStrategy == LakehousePartitionStrategy.DailyCommitUtc
            ? "，分区 day(commit_timestamp_utc)"
            : string.Empty;

        return new CdcLakehouseHistoryDto
        {
            StreamId = stream.Id,
            Destination = destination,
            TableFormat = LakehouseTableFormat.ApacheIceberg,
            PartitionStrategy = stream.LakehousePartitionStrategy,
            DeltaLogDirectory = metadataDir,
            CurrentVersion = currentSeq,
            SelectedVersion = selectedSeq,
            TotalCommitCount = snapshots.Count,
            SelectedActiveFileCount = activeFiles.Count,
            SelectedTotalBytes = totalBytes,
            SelectedTotalRecordCount = totalRecords,
            Commits = recentCommits,
            ActiveFiles = activeFiles,
            TimeTravelQuerySql = timeTravelSql,
            Summary = $"Apache Iceberg v2 · v{selectedSeq}（当前最新 v{currentSeq}，Snapshot {selectedSnap.SnapshotId}{partitionTag}，活跃 Parquet 分片 {activeFiles.Count} 个，记录 {totalRecords} 条）。",
        };
    }

    private static List<ParsedIcebergSnapshot> ParseSnapshots(
        string metadataJson,
        ObjectStorageSinkConfig config,
        string destination)
    {
        var list = new List<ParsedIcebergSnapshot>();
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty("snapshots", out var snaps) || snaps.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var snap in snaps.EnumerateArray())
            {
                var seq = snap.TryGetProperty("sequence-number", out var seqProp) && seqProp.TryGetInt64(out var s) ? s : 0L;
                var sid = snap.TryGetProperty("snapshot-id", out var sidProp) && sidProp.TryGetInt64(out var id) ? id : 0L;
                if (seq <= 0 || sid <= 0)
                {
                    continue;
                }

                var tsMs = snap.TryGetProperty("timestamp-ms", out var tsProp) && tsProp.TryGetInt64(out var ms) ? ms : 0L;
                var tsUtc = DateTimeOffset.FromUnixTimeMilliseconds(tsMs).UtcDateTime;

                var operation = "APPEND";
                string? engineInfo = null;
                string? swapmidRunId = null;
                string? snapshotMode = null;
                var addedFiles = 0;
                var removedFiles = 0;
                long addedRecords = 0;

                if (snap.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Object)
                {
                    if (summary.TryGetProperty("operation", out var opProp) && opProp.ValueKind == JsonValueKind.String)
                    {
                        operation = (opProp.GetString() ?? "append").ToUpperInvariant();
                    }

                    engineInfo = ReadSummaryString(summary, "engine-name");
                    swapmidRunId = ReadSummaryString(summary, "swapmid-run-id");
                    snapshotMode = ReadSummaryString(summary, "swapmid-snapshot-mode");
                    addedFiles = (int)ReadSummaryLong(summary, "added-data-files");
                    removedFiles = (int)ReadSummaryLong(summary, "deleted-data-files");
                    addedRecords = ReadSummaryLong(summary, "added-records");
                }

                var manifestListUri = snap.TryGetProperty("manifest-list", out var mlProp) && mlProp.ValueKind == JsonValueKind.String
                    ? mlProp.GetString()
                    : null;

                list.Add(new ParsedIcebergSnapshot(
                    sid,
                    manifestListUri,
                    new CdcDeltaCommitSummaryDto
                    {
                        Version = seq,
                        TimestampUtc = tsUtc,
                        Operation = operation,
                        EngineInfo = engineInfo,
                        SwapMidRunId = swapmidRunId,
                        SnapshotMode = snapshotMode,
                        AddedFileCount = addedFiles,
                        RemovedFileCount = removedFiles,
                        TotalRecordCount = addedRecords,
                        DeltaCommitKey = config.BuildIcebergMetadataKey(destination, seq),
                    }));
            }
        }
        catch (JsonException)
        {
            return list;
        }

        return list.OrderBy(x => x.Summary.Version).ToList();
    }

    private static async Task<List<CdcDeltaActiveFileDto>> ReadSnapshotActiveFilesAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        long snapshotId,
        string? manifestListUri,
        CancellationToken cancellationToken)
    {
        var active = await IcebergV2CommitWriter.LoadSnapshotActiveFilesAsync(
            httpClientFactory,
            configuration,
            config,
            destination,
            snapshotId,
            manifestListUri,
            cancellationToken);

        return active
            .Select(f => new CdcDeltaActiveFileDto
            {
                RelativePath = f.RelativePath,
                ObjectKey = f.ObjectKey,
                SizeBytes = f.FileSizeInBytes,
                RecordCount = f.RecordCount,
                AddedAtVersion = f.AddedAtSequence,
            })
            .OrderBy(f => f.AddedAtVersion)
            .ThenBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static string BuildIcebergTimeTravelSql(
        ObjectStorageSinkConfig config,
        string tableDir,
        long selectedSeq,
        long snapshotId,
        LakehousePartitionStrategy partitionStrategy,
        IReadOnlyList<CdcDeltaActiveFileDto> activeFiles)
    {
        var s3Base = string.IsNullOrEmpty(tableDir)
            ? $"s3://{config.Bucket}"
            : $"s3://{config.Bucket}/{tableDir}";
        var metadataUri = $"{s3Base}/metadata/v{selectedSeq}.metadata.json";

        var duckdbFiles = activeFiles.Count > 0
            ? string.Join(",\n  ", activeFiles.Take(8).Select(f => $"'s3://{config.Bucket}/{f.ObjectKey}'"))
            : $"'{s3Base}/*.parquet'";
        var suffix = activeFiles.Count > 8 ? $"\n  -- (+ {activeFiles.Count - 8} more part files at v{selectedSeq})" : string.Empty;
        var todayUtc = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var partitionPruneSnippet = partitionStrategy == LakehousePartitionStrategy.DailyCommitUtc
            ? $"-- DuckDB Iceberg v2 Hidden Partition Pruning (day(commit_timestamp_utc))\n" +
              $"SELECT * FROM iceberg_scan('{metadataUri}') WHERE commit_timestamp_utc >= TIMESTAMPTZ '{todayUtc} 00:00:00+00' LIMIT 100;\n\n"
            : string.Empty;

        return
            partitionPruneSnippet +
            $"-- DuckDB Apache Iceberg v2 Metadata Scan (v{selectedSeq} / snapshot {snapshotId})\n" +
            $"SELECT * FROM iceberg_scan('{metadataUri}') LIMIT 100;\n\n" +
            $"-- Trino / Apache Spark SQL Time-Travel (Snapshot ID {snapshotId})\n" +
            $"SELECT * FROM iceberg_catalog.ns.table FOR VERSION AS OF {snapshotId} LIMIT 100;\n\n" +
            $"-- DuckDB Exact Point-in-Time Parquet Files (v{selectedSeq})\n" +
            $"SELECT * FROM read_parquet([\n  {duckdbFiles}{suffix}\n]) LIMIT 100;";
    }

    private static string? ReadSummaryString(JsonElement summary, string key)
        => summary.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static long ReadSummaryLong(JsonElement summary, string key)
    {
        if (!summary.TryGetProperty(key, out var prop))
        {
            return 0L;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var n))
        {
            return n;
        }

        if (prop.ValueKind == JsonValueKind.String &&
            long.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0L;
    }

    private sealed record ParsedIcebergSnapshot(long SnapshotId, string? ManifestListUri, CdcDeltaCommitSummaryDto Summary);
}
