using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SwapMid.Monitoring;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// 读取 S3/MinIO `_delta_log/{version:D20}.json` 事务提交日志并回放 Time-Travel 活跃 Parquet 分片视图。
/// </summary>
public sealed class DeltaLakeHistoryReader : ITransientDependency
{
    private const int MaxReplayCommits = 500;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public DeltaLakeHistoryReader(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<CdcLakehouseHistoryDto> ReadAsync(
        CdcStreamDefinition stream,
        DataSource sink,
        long? requestedVersion = null,
        int maxCommits = CdcStreamConsts.DefaultLakehouseHistoryMaxCommits,
        CancellationToken cancellationToken = default)
    {
        if (stream.TargetMode != CdcTargetMode.ExternalSink ||
            sink.Type != DataSourceType.ObjectStorage ||
            string.IsNullOrWhiteSpace(stream.TargetSinkDestination))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "仅已绑定 ObjectStorage 接收端的 CDC 流支持查询 Lakehouse Time-Travel 提交历史。");
        }

        var config = ObjectStorageSinkConfigContract.ParseForWrite(sink.Config, requireSecretKey: false);
        var destination = stream.TargetSinkDestination.Trim();
        var deltaDir = config.BuildDeltaLogDirectory(destination);
        var tableDir = config.BuildDestinationDirectory(destination).TrimEnd('/');
        var boundedMax = Math.Clamp(
            maxCommits <= 0 ? CdcStreamConsts.DefaultLakehouseHistoryMaxCommits : maxCommits,
            1,
            CdcStreamConsts.MaxLakehouseHistoryMaxCommits);

        var latestJson = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
            _httpClientFactory,
            _configuration,
            config,
            config.BuildLatestManifestKey(destination),
            cancellationToken);
        var latestDoc = !string.IsNullOrWhiteSpace(latestJson)
            ? TryDeserializeLatest(latestJson)
            : null;

        if (stream.LakehouseTableFormat == LakehouseTableFormat.ApacheIceberg)
        {
            return await IcebergV2MetadataReader.ReadAsync(
                _httpClientFactory,
                _configuration,
                stream,
                config,
                destination,
                latestDoc,
                requestedVersion,
                boundedMax,
                cancellationToken);
        }

        if (stream.LakehouseTableFormat != LakehouseTableFormat.DeltaLake)
        {
            return new CdcLakehouseHistoryDto
            {
                StreamId = stream.Id,
                Destination = destination,
                TableFormat = stream.LakehouseTableFormat,
                DeltaLogDirectory = null,
                CurrentVersion = null,
                SelectedVersion = null,
                TotalCommitCount = 0,
                Summary = "当前 CDC 流未开启 Apache Delta Lake（_delta_log）或 Apache Iceberg v2 表协议，可在配置中开启后查看 Time-Travel 提交时间线。",
            };
        }

        var upperBound = latestDoc?.LatestDeltaVersion is >= 0
            ? Math.Min(latestDoc.LatestDeltaVersion.Value, MaxReplayCommits - 1)
            : MaxReplayCommits - 1;

        var parsedCommits = new List<ParsedDeltaCommit>();
        for (var version = 0L; version <= upperBound; version++)
        {
            var commitKey = config.BuildDeltaCommitKey(destination, version);
            var ndjson = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
                _httpClientFactory,
                _configuration,
                config,
                commitKey,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(ndjson))
            {
                break;
            }

            parsedCommits.Add(ParseCommitNdjson(version, commitKey, ndjson));
        }

        if (parsedCommits.Count == 0)
        {
            if (requestedVersion.HasValue)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"目标表尚未生成 Delta Lake _delta_log 提交记录，版本 v{requestedVersion.Value} 不存在。");
            }

            return new CdcLakehouseHistoryDto
            {
                StreamId = stream.Id,
                Destination = destination,
                TableFormat = stream.LakehouseTableFormat,
                DeltaLogDirectory = deltaDir,
                CurrentVersion = null,
                SelectedVersion = null,
                TotalCommitCount = 0,
                Summary = "目标表尚未产生 _delta_log 提交，请先执行基线快照或触发一次增量捕获。",
            };
        }

        var currentVersion = parsedCommits[^1].Summary.Version;
        var selectedVersion = requestedVersion ?? currentVersion;
        if (selectedVersion < 0 || selectedVersion > currentVersion)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"请求的 Delta Lake Time-Travel 版本 v{selectedVersion} 超出当前已提交范围（v0..v{currentVersion}）。");
        }

        var activeMap = new Dictionary<string, CdcDeltaActiveFileDto>(StringComparer.Ordinal);
        foreach (var commit in parsedCommits)
        {
            if (commit.Summary.Version > selectedVersion)
            {
                break;
            }

            foreach (var removedPath in commit.RemovedPaths)
            {
                activeMap.Remove(removedPath);
            }

            foreach (var added in commit.AddedFiles)
            {
                var rel = added.RelativePath.TrimStart('/');
                var fullObjectKey = string.IsNullOrEmpty(tableDir) ? rel : $"{tableDir}/{rel}";
                activeMap[rel] = new CdcDeltaActiveFileDto
                {
                    RelativePath = rel,
                    ObjectKey = fullObjectKey,
                    SizeBytes = added.SizeBytes,
                    RecordCount = added.RecordCount,
                    AddedAtVersion = commit.Summary.Version,
                };
            }
        }

        var activeFiles = activeMap.Values
            .OrderBy(f => f.AddedAtVersion)
            .ThenBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
        var totalBytes = activeFiles.Sum(f => f.SizeBytes);
        var totalRecords = activeFiles.Sum(f => f.RecordCount ?? 0);
        if (!requestedVersion.HasValue)
        {
            PlatformMetrics.CdcLakehouseActiveBytes.WithLabels(stream.Name, "delta_lake").Set(totalBytes);
            PlatformMetrics.CdcLakehouseActiveFiles.WithLabels(stream.Name, "delta_lake").Set(activeFiles.Count);
            PlatformMetrics.CdcLakehouseSnapshotsCount.WithLabels(stream.Name, "delta_lake").Set(parsedCommits.Count);
        }

        var recentCommits = parsedCommits
            .OrderByDescending(c => c.Summary.Version)
            .Take(boundedMax)
            .Select(c => c.Summary)
            .ToList();

        var timeTravelSql = BuildTimeTravelSql(config, tableDir, selectedVersion, activeFiles);

        return new CdcLakehouseHistoryDto
        {
            StreamId = stream.Id,
            Destination = destination,
            TableFormat = stream.LakehouseTableFormat,
            DeltaLogDirectory = deltaDir,
            CurrentVersion = currentVersion,
            SelectedVersion = selectedVersion,
            TotalCommitCount = parsedCommits.Count,
            SelectedActiveFileCount = activeFiles.Count,
            SelectedTotalBytes = totalBytes,
            SelectedTotalRecordCount = totalRecords,
            Commits = recentCommits,
            ActiveFiles = activeFiles,
            TimeTravelQuerySql = timeTravelSql,
            Summary = $"Delta Lake v{selectedVersion}（当前最新 v{currentVersion}，活跃 Parquet 分片 {activeFiles.Count} 个，记录 {totalRecords} 条）。",
        };
    }

    private static string BuildTimeTravelSql(
        ObjectStorageSinkConfig config,
        string tableDir,
        long selectedVersion,
        IReadOnlyList<CdcDeltaActiveFileDto> activeFiles)
    {
        var s3Base = string.IsNullOrEmpty(tableDir)
            ? $"s3://{config.Bucket}"
            : $"s3://{config.Bucket}/{tableDir}";
        var s3aBase = string.IsNullOrEmpty(tableDir)
            ? $"s3a://{config.Bucket}"
            : $"s3a://{config.Bucket}/{tableDir}";

        var duckdbFiles = activeFiles.Count > 0
            ? string.Join(",\n  ", activeFiles.Take(8).Select(f => $"'s3://{config.Bucket}/{f.ObjectKey}'"))
            : $"'{s3Base}/*.parquet'";
        var suffix = activeFiles.Count > 8 ? $"\n  -- (+ {activeFiles.Count - 8} more part files at v{selectedVersion})" : string.Empty;

        return
            $"-- Spark SQL Time-Travel (VERSION AS OF {selectedVersion})\n" +
            $"SELECT * FROM delta.`{s3aBase}` VERSION AS OF {selectedVersion} LIMIT 100;\n\n" +
            $"-- Polars Delta Time-Travel\n" +
            $"-- pl.read_delta(\"{s3Base}\", version={selectedVersion})\n\n" +
            $"-- DuckDB Exact Point-in-Time Snapshot Files (v{selectedVersion})\n" +
            $"SELECT * FROM read_parquet([\n  {duckdbFiles}{suffix}\n]) LIMIT 100;";
    }

    private static CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument? TryDeserializeLatest(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument>(json, CdcObjectStorageLakehouseWriter.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ParsedDeltaCommit ParseCommitNdjson(long version, string commitKey, string ndjson)
    {
        var timestampUtc = DateTimeOffset.UnixEpoch.UtcDateTime;
        var operation = "WRITE";
        string? engineInfo = null;
        string? swapmidRunId = null;
        string? snapshotMode = null;
        var added = new List<ParsedAddedFile>();
        var removed = new List<string>();
        long totalRecordsInCommit = 0;

        foreach (var rawLine in ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var doc = JsonDocument.Parse(rawLine);
            var root = doc.RootElement;
            if (root.TryGetProperty("commitInfo", out var info))
            {
                if (info.TryGetProperty("timestamp", out var tsProp) && tsProp.TryGetInt64(out var tsMs))
                {
                    timestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(tsMs).UtcDateTime;
                }

                if (info.TryGetProperty("operation", out var opProp) && opProp.ValueKind == JsonValueKind.String)
                {
                    operation = opProp.GetString() ?? operation;
                }

                if (info.TryGetProperty("engineInfo", out var engProp) && engProp.ValueKind == JsonValueKind.String)
                {
                    engineInfo = engProp.GetString();
                }

                if (info.TryGetProperty("txnId", out var txnProp) && txnProp.ValueKind == JsonValueKind.String)
                {
                    swapmidRunId = txnProp.GetString();
                }

                if (info.TryGetProperty("operationParameters", out var parms) && parms.ValueKind == JsonValueKind.Object)
                {
                    if (string.IsNullOrWhiteSpace(swapmidRunId) &&
                        parms.TryGetProperty("swapmidRunId", out var runProp) &&
                        runProp.ValueKind == JsonValueKind.String)
                    {
                        swapmidRunId = runProp.GetString();
                    }

                    if (parms.TryGetProperty("snapshotMode", out var snapProp) && snapProp.ValueKind == JsonValueKind.String)
                    {
                        snapshotMode = snapProp.GetString();
                    }
                }
            }
            else if (root.TryGetProperty("add", out var addElem))
            {
                var path = addElem.TryGetProperty("path", out var p) ? p.GetString() : null;
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var size = addElem.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0L;
                var numRecords = TryParseNumRecords(addElem);
                if (numRecords.HasValue)
                {
                    totalRecordsInCommit += numRecords.Value;
                }

                added.Add(new ParsedAddedFile(path, size, numRecords));
            }
            else if (root.TryGetProperty("remove", out var remElem))
            {
                var path = remElem.TryGetProperty("path", out var p) ? p.GetString() : null;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    removed.Add(path.TrimStart('/'));
                }
            }
        }

        return new ParsedDeltaCommit(
            new CdcDeltaCommitSummaryDto
            {
                Version = version,
                TimestampUtc = timestampUtc,
                Operation = operation,
                EngineInfo = engineInfo,
                SwapMidRunId = swapmidRunId,
                SnapshotMode = snapshotMode,
                AddedFileCount = added.Count,
                RemovedFileCount = removed.Count,
                TotalRecordCount = totalRecordsInCommit,
                DeltaCommitKey = commitKey,
            },
            added,
            removed);
    }

    private static long? TryParseNumRecords(JsonElement addElem)
    {
        if (!addElem.TryGetProperty("stats", out var statsElem) || statsElem.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = statsElem.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var statsDoc = JsonDocument.Parse(raw);
            if (statsDoc.RootElement.TryGetProperty("numRecords", out var nr) && nr.TryGetInt64(out var count))
            {
                return count;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private sealed record ParsedAddedFile(string RelativePath, long SizeBytes, long? RecordCount);

    private sealed record ParsedDeltaCommit(
        CdcDeltaCommitSummaryDto Summary,
        IReadOnlyList<ParsedAddedFile> AddedFiles,
        IReadOnlyList<string> RemovedPaths);
}
