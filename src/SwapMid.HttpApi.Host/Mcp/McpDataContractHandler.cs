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
/// AI SRE 只读/受控数据契约 SLA 态势评估处理器（<c>evaluate_cdc_data_contracts</c>）。
/// </summary>
internal static class McpDataContractHandler
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
        var onlyBreached = McpJsonRpcHelpers.ReadOptionalBool(args, "onlyBreached");
        var probeLive = McpJsonRpcHelpers.ReadOptionalBool(args, "probeLiveUpstream");

        var canManage = false;
        if (probeLive)
        {
            var manageAuth = await authorizationService.AuthorizeAsync(
                user, null, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
            canManage = manageAuth.Succeeded;
        }

        var page = await service.GetListAsync(new GetCdcStreamListInput
        {
            Filter = filter,
            MaxResultCount = limit,
            SkipCount = 0,
        });

        var evaluations = new List<object>(page.Items.Count);
        int breachedCount = 0, warningCount = 0, healthyCount = 0, unconfiguredCount = 0;

        foreach (var stream in page.Items)
        {
            CdcContractEvaluationDto? liveEval = null;
            if (canManage && (stream.MaxFreshnessLagSeconds.HasValue || stream.MaxAllowedWalLagBytes.HasValue || !string.IsNullOrWhiteSpace(stream.RequiredColumns)))
            {
                try
                {
                    liveEval = await service.EvaluateContractAsync(stream.Id, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    liveEval = null;
                }
            }

            var status = liveEval?.Status ?? stream.ContractStatus;
            switch (status)
            {
                case CdcContractStatus.Breached: breachedCount++; break;
                case CdcContractStatus.Warning: warningCount++; break;
                case CdcContractStatus.Healthy: healthyCount++; break;
                default: unconfiguredCount++; break;
            }

            if (onlyBreached && status != CdcContractStatus.Breached)
            {
                continue;
            }

            evaluations.Add(new
            {
                stream.Id,
                stream.Name,
                stream.Status,
                contractStatus = status.ToString(),
                contractViolated = liveEval?.Violated ?? stream.ContractViolated,
                freshnessLagSeconds = liveEval?.FreshnessLagSeconds ?? stream.FreshnessLagSeconds,
                stream.MaxFreshnessLagSeconds,
                walLagBytes = liveEval?.WalLagBytes,
                stream.MaxAllowedWalLagBytes,
                stream.RequiredColumns,
                missingRequiredColumns = liveEval?.MissingRequiredColumns ?? new List<string>(),
                stream.SchemaDriftDetected,
                stream.CatalogLastSyncedVersion,
                summary = liveEval?.Summary ?? stream.ContractViolationSummary ?? "OK",
                violations = liveEval?.Violations ?? new List<CdcContractViolationItemDto>(),
            });
        }

        var hints = new List<string>();
        if (breachedCount > 0)
        {
            hints.Add("Investigate breached streams via diagnose_cdc_streams(includePreflight=true), accept intentional schema drift via POST /api/v1/cdc-streams/{id}/accept-schema, or inspect historical Delta snapshots via lakehouse-history/lakehouse-preview.");
        }
        if (unconfiguredCount > 0 && breachedCount == 0)
        {
            hints.Add("Configure maxFreshnessLagSeconds, requiredColumns, or maxAllowedWalLagBytes on active CDC streams to enable automated SLA alerting.");
        }

        return new
        {
            totalStreams = page.TotalCount,
            liveProbeApplied = canManage,
            summary = new
            {
                breachedCount,
                warningCount,
                healthyCount,
                unconfiguredCount,
            },
            contracts = evaluations,
            remediationHints = hints,
        };
    }
}
