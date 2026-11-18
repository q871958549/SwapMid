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

internal static class McpCdcDiagnosticsHandler
{
    internal static async Task<object> ExecuteAsync(
        IServiceProvider serviceProvider,
        IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var service = serviceProvider.GetRequiredService<ICdcStreamAppService>();
        var filter = McpJsonRpcHelpers.ReadOptionalString(args, "filter");
        var limit = McpJsonRpcHelpers.ReadClampedInt(args, "limit", defaultValue: 20, min: 1, max: 50);
        var includePreflight = args.ValueKind == JsonValueKind.Object &&
                               args.TryGetProperty("includePreflight", out var pfElem) &&
                               pfElem.ValueKind == JsonValueKind.True;

        var canManage = includePreflight &&
                        (await authorizationService.AuthorizeAsync(user, null, SwapMidPermissions.DataIntegration.CdcStreams.Manage)).Succeeded;

        var page = await service.GetListAsync(new GetCdcStreamListInput
        {
            Filter = filter,
            MaxResultCount = limit,
            SkipCount = 0,
        });

        var items = new List<object>(page.Items.Count);
        foreach (var stream in page.Items)
        {
            CdcPreflightResultDto? preflight = null;
            if (canManage)
            {
                try
                {
                    preflight = await service.PreflightAsync(stream.Id, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    preflight = null;
                }
            }

            items.Add(new
            {
                stream.Id,
                stream.Name,
                stream.SourceType,
                stream.Status,
                stream.MonitoredTables,
                stream.PollIntervalSeconds,
                stream.MaxBatchSize,
                stream.TargetMode,
                stream.TargetSinkName,
                stream.TargetSinkDestination,
                stream.TargetSinkKeyColumns,
                stream.TargetDeletePolicy,
                targetFieldMappingsCount = stream.TargetFieldMappings.Count,
                stream.SoftDeleteColumn,
                stream.SoftDeleteTimestampColumn,
                stream.LakehouseCompactMinFiles,
                stream.LakehouseTableFormat,
                stream.SchemaDriftPolicy,
                stream.SchemaBaselineHash,
                stream.SchemaDriftDetected,
                stream.SchemaDriftSummary,
                stream.SchemaDriftDetectedAt,
                stream.CheckpointLsn,
                stream.InitialSnapshotEnabled,
                stream.SnapshotStatus,
                stream.SnapshotRowCount,
                stream.LastCapturedAt,
                stream.LastCapturedCount,
                stream.TotalCapturedCount,
                stream.LastErrorCode,
                preflight = preflight == null
                    ? null
                    : new
                    {
                        preflight.IsReady,
                        preflight.WalLevel,
                        preflight.WalLagBytes,
                        preflight.ConfirmedFlushLsn,
                        preflight.CurrentWalLsn,
                        preflight.TableReplicaIdentities,
                        preflight.SchemaDriftDetected,
                        preflight.SchemaDriftSummary,
                        preflight.CurrentSchemaHash,
                        preflight.MirrorCompatibility,
                        preflight.Diagnostics,
                    },
            });
        }

        return new
        {
            totalCount = page.TotalCount,
            preflightIncluded = canManage,
            streams = items,
        };
    }
}
