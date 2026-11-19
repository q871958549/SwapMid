using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SwapMid.DataIntegration;

namespace SwapMid.Mcp;

/// <summary>
/// MCP Tool 10（只读诊断）：检查 Delta Lake / Apache Iceberg v2 表快照时间线、分区策略、活跃 Parquet 分片统计及 DuckDB/Trino/Spark Time-Travel SQL。
/// </summary>
internal static class McpLakehouseSnapshotHandler
{
    internal static async Task<object> ExecuteAsync(
        IServiceProvider serviceProvider,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var streamService = serviceProvider.GetRequiredService<ICdcStreamAppService>();
        var streamId = McpJsonRpcHelpers.ReadOptionalGuid(args, "streamId");
        var maxCommits = McpJsonRpcHelpers.ReadClampedInt(args, "maxCommits", defaultValue: 10, min: 1, max: 50);

        long? version = null;
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty("version", out var vElem) &&
            vElem.ValueKind == JsonValueKind.Number &&
            vElem.TryGetInt64(out var parsedVer) &&
            parsedVer >= 0)
        {
            version = parsedVer;
        }

        if (!streamId.HasValue)
        {
            var page = await streamService.GetListAsync(new GetCdcStreamListInput
            {
                MaxResultCount = 50,
                SkipCount = 0,
            });
            var lakehouseStreams = page.Items
                .Where(s => s.TargetMode == CdcTargetMode.ExternalSink &&
                            s.TargetSinkType == DataSourceType.ObjectStorage &&
                            s.LakehouseTableFormat is (LakehouseTableFormat.DeltaLake or LakehouseTableFormat.ApacheIceberg))
                .ToList();
            if (lakehouseStreams.Count == 0)
            {
                return new
                {
                    lakehouseStreamCount = 0,
                    message = "No Delta Lake or Apache Iceberg v2 CDC streams configured.",
                    snapshots = Array.Empty<object>(),
                };
            }

            var summaries = new List<object>(lakehouseStreams.Count);
            foreach (var s in lakehouseStreams.Take(10))
            {
                var hist = await streamService.GetLakehouseHistoryAsync(
                    s.Id,
                    new GetCdcLakehouseHistoryInput { Version = null, MaxCommits = Math.Min(maxCommits, 5) },
                    cancellationToken);
                summaries.Add(FormatHistoryPayload(s, hist));
            }

            return new
            {
                lakehouseStreamCount = lakehouseStreams.Count,
                snapshots = summaries,
            };
        }

        var stream = await streamService.GetAsync(streamId.Value);
        var history = await streamService.GetLakehouseHistoryAsync(
            stream.Id,
            new GetCdcLakehouseHistoryInput { Version = version, MaxCommits = maxCommits },
            cancellationToken);
        return FormatHistoryPayload(stream, history);
    }

    private static object FormatHistoryPayload(CdcStreamDto stream, CdcLakehouseHistoryDto history) => new
    {
        streamId = stream.Id,
        streamName = stream.Name,
        sourceType = stream.SourceType,
        tableFormat = history.TableFormat,
        partitionStrategy = history.PartitionStrategy,
        destination = history.Destination,
        metadataDirectory = history.DeltaLogDirectory,
        currentVersion = history.CurrentVersion,
        selectedVersion = history.SelectedVersion,
        totalCommitCount = history.TotalCommitCount,
        selectedActiveFileCount = history.SelectedActiveFileCount,
        selectedTotalBytes = history.SelectedTotalBytes,
        selectedTotalRecordCount = history.SelectedTotalRecordCount,
        summary = history.Summary,
        timeTravelQuerySql = history.TimeTravelQuerySql,
        commits = history.Commits.Select(c => new
        {
            c.Version,
            c.Operation,
            c.AddedFileCount,
            c.RemovedFileCount,
            c.TotalRecordCount,
            c.TimestampUtc,
            c.EngineInfo,
        }),
        activeFiles = history.ActiveFiles.Select(f => new
        {
            f.RelativePath,
            f.SizeBytes,
            f.RecordCount,
            f.AddedAtVersion,
        }),
    };
}
