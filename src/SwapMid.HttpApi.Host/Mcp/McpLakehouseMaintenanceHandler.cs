using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SwapMid.DataIntegration;
using SwapMid.Permissions;

namespace SwapMid.Mcp;

/// <summary>
/// MCP Tool 12（只读维护规划）：对 Apache Iceberg v2 表执行零写入 Dry-Run 维护规划，
/// 结合命名标签（refs）、Equality Delete 累积与快照保留策略输出压缩与 expireSnapshots 建议。
/// </summary>
internal static class McpLakehouseMaintenanceHandler
{
    internal static async Task<object> ExecuteAsync(
        IServiceProvider serviceProvider,
        IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var streamService = serviceProvider.GetRequiredService<ICdcStreamAppService>();
        var streamId = McpJsonRpcHelpers.ReadOptionalGuid(args, "streamId");
        var retainLast = McpJsonRpcHelpers.ReadClampedInt(args, "retainLast", defaultValue: 2, min: 1, max: 100);
        var olderThanMinutes = McpJsonRpcHelpers.ReadClampedInt(args, "olderThanMinutes", defaultValue: 0, min: 0, max: 525_600);
        var canManage = (await authorizationService.AuthorizeAsync(user, null, SwapMidPermissions.DataIntegration.CdcStreams.Manage)).Succeeded;

        var targets = new List<CdcStreamDto>();
        if (streamId.HasValue)
        {
            targets.Add(await streamService.GetAsync(streamId.Value));
        }
        else
        {
            var page = await streamService.GetListAsync(new GetCdcStreamListInput { MaxResultCount = 50, SkipCount = 0 });
            targets.AddRange(page.Items
                .Where(s => s.TargetMode == CdcTargetMode.ExternalSink &&
                            s.TargetSinkType == DataSourceType.ObjectStorage &&
                            s.LakehouseTableFormat == LakehouseTableFormat.ApacheIceberg)
                .Take(10));
        }

        if (targets.Count == 0)
        {
            return new
            {
                icebergStreamCount = 0,
                dryRun = true,
                plans = Array.Empty<object>(),
                message = "No Apache Iceberg v2 CDC streams configured.",
            };
        }

        var plans = new List<object>(targets.Count);
        foreach (var stream in targets)
        {
            var history = await streamService.GetLakehouseHistoryAsync(
                stream.Id,
                new GetCdcLakehouseHistoryInput { Version = null, MaxCommits = 50 },
                cancellationToken);

            CdcLakehouseExpireResultDto? expirePlan = null;
            if (canManage && history.CurrentVersion.HasValue)
            {
                expirePlan = await streamService.ExpireLakehouseSnapshotsAsync(
                    stream.Id,
                    new ExpireCdcLakehouseSnapshotsInput
                    {
                        RetainLast = retainLast,
                        OlderThanMinutes = olderThanMinutes,
                        DeleteOrphanedFiles = false,
                        DryRun = true,
                    },
                    cancellationToken);
            }

            var recommendations = new List<string>();
            if (history.SelectedEqualityDeleteFileCount > 0)
            {
                recommendations.Add(
                    $"Active Merge-on-Read Equality Deletes detected ({history.SelectedEqualityDeleteFileCount} file(s) / {history.SelectedEqualityDeleteRecordCount} keys). Consider running POST /api/v1/cdc-streams/{stream.Id}/compact-lakehouse to materialize clean Copy-on-Write Parquet.");
            }

            if (history.SelectedActiveFileCount >= Math.Max(2, stream.LakehouseCompactMinFiles ?? 5))
            {
                recommendations.Add(
                    $"Active data file count ({history.SelectedActiveFileCount}) reached compaction threshold; run compact-lakehouse to reduce read amplification.");
            }

            var expirableEstimate = expirePlan?.ExpiredSnapshotCount
                ?? Math.Max(0, history.TotalCommitCount - retainLast);
            if (expirableEstimate > 0)
            {
                recommendations.Add(
                    $"Found {expirableEstimate} historical snapshot(s) eligible for expireSnapshots (retainLast={retainLast}, pinnedTags={history.Refs.Count(r => !string.Equals(r.Name, "main", StringComparison.Ordinal))}).");
            }

            var mainRef = history.Refs.FirstOrDefault(r => string.Equals(r.Name, "main", StringComparison.Ordinal));
            var pendingBranches = history.Refs
                .Where(r => string.Equals(r.Type, "branch", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(r.Name, "main", StringComparison.Ordinal) &&
                            r.SnapshotId != mainRef?.SnapshotId)
                .ToList();
            foreach (var br in pendingBranches)
            {
                recommendations.Add(
                    $"Unpromoted WAP branch '{br.Name}' (v{br.Version}) is ahead of main (v{mainRef?.Version?.ToString() ?? "none"}). Preview rows and call POST /api/v1/cdc-streams/{stream.Id}/promote-lakehouse-branch when audit passes.");
            }

            if (recommendations.Count == 0)
            {
                recommendations.Add("Iceberg v2 table layout is healthy; no compaction or expiration required.");
            }

            plans.Add(new
            {
                streamId = stream.Id,
                streamName = stream.Name,
                destination = history.Destination,
                writeMode = history.WriteMode,
                writeBranch = stream.LakehouseWriteBranch ?? "main",
                maintenanceCron = stream.LakehouseMaintenanceCron,
                partitionStrategy = history.PartitionStrategy,
                currentVersion = history.CurrentVersion,
                totalSnapshots = history.TotalCommitCount,
                activeDataFiles = history.SelectedActiveFileCount,
                equalityDeleteFiles = history.SelectedEqualityDeleteFileCount,
                equalityDeleteKeys = history.SelectedEqualityDeleteRecordCount,
                refs = history.Refs.Select(r => new { r.Name, r.Type, r.SnapshotId, r.Version }),
                expireDryRun = expirePlan is null ? null : new
                {
                    expirePlan.RetainedSnapshotCount,
                    expirePlan.ExpiredSnapshotCount,
                    expirePlan.PinnedByRefCount,
                    expirePlan.ExpiredVersions,
                    expirePlan.CandidateOrphanFileCount,
                    expirePlan.ReclaimableBytes,
                    expirePlan.Summary,
                },
                recommendations,
            });
        }

        return new
        {
            icebergStreamCount = targets.Count,
            dryRun = true,
            retainLast,
            olderThanMinutes,
            plans,
        };
    }
}
