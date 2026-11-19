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
/// MCP Tool 9（只读诊断）：巡检 Push Webhook CDC 投递与死信重放台账，汇总 401 鉴权拦截、409/500 可重放死信及运维修复建议。
/// </summary>
internal static class McpWebhookDeliveryDiagnosticsHandler
{
    internal static async Task<object> ExecuteAsync(
        IServiceProvider serviceProvider,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var streamService = serviceProvider.GetRequiredService<ICdcStreamAppService>();
        var webhookService = serviceProvider.GetRequiredService<ICdcWebhookIngressService>();

        var streamId = McpJsonRpcHelpers.ReadOptionalGuid(args, "streamId");
        var limit = McpJsonRpcHelpers.ReadClampedInt(args, "limit", defaultValue: 20, min: 1, max: 50);
        var onlyReplayable = args.ValueKind == JsonValueKind.Object &&
                             args.TryGetProperty("onlyReplayable", out var repElem) &&
                             repElem.ValueKind == JsonValueKind.True;

        var targetStreams = new List<CdcStreamDto>();
        if (streamId.HasValue)
        {
            targetStreams.Add(await streamService.GetAsync(streamId.Value));
        }
        else
        {
            var page = await streamService.GetListAsync(new GetCdcStreamListInput
            {
                MaxResultCount = 50,
                SkipCount = 0,
            });
            targetStreams.AddRange(page.Items.Where(s => s.SourceType == CdcSourceType.PushWebhook));
        }

        var reports = new List<object>(targetStreams.Count);
        var totalReplayable = 0;
        var totalUnauthorized = 0;
        var totalFailed = 0;

        foreach (var stream in targetStreams)
        {
            if (stream.SourceType != CdcSourceType.PushWebhook)
            {
                continue;
            }

            var ledger = await webhookService.GetDeliveriesAsync(stream.Id, limit, cancellationToken);
            var filtered = onlyReplayable
                ? ledger.Items.Where(i => i.CanReplay).ToList()
                : ledger.Items;
            var unauthorizedCount = ledger.Items.Count(i => i.Outcome == CdcWebhookIngestOutcome.Unauthorized);
            var failedCount = ledger.Items.Count(i => i.Outcome == CdcWebhookIngestOutcome.Failed);

            totalReplayable += ledger.ReplayableCount;
            totalUnauthorized += unauthorizedCount;
            totalFailed += failedCount;

            var guidance = new List<string>();
            if (ledger.ReplayableCount > 0)
            {
                guidance.Add(
                    stream.Status != CdcStreamStatus.Active
                        ? $"Stream '{stream.Name}' is {stream.Status}; activate it first (PUT /api/v1/cdc-streams/{stream.Id:D}/activate), then replay {ledger.ReplayableCount} dead-letter batch(es)."
                        : $"Stream '{stream.Name}' has {ledger.ReplayableCount} replayable dead-letter batch(es); replay via POST /api/v1/cdc-webhooks/{stream.Id:D}/deliveries/{{deliveryId}}/replay.");
            }

            if (unauthorizedCount > 0)
            {
                guidance.Add($"Detected {unauthorizedCount} HMAC-SHA256 authentication rejection(s) (401); verify upstream X-SwapMid-Signature secret alignment. Raw payloads were not stored.");
            }

            if (stream.SchemaDriftDetected)
            {
                guidance.Add($"Upstream webhook schema drift detected: {stream.SchemaDriftSummary}");
            }

            reports.Add(new
            {
                streamId = stream.Id,
                streamName = stream.Name,
                status = stream.Status,
                webhookIngressPath = $"/api/v1/cdc-webhooks/{stream.Id:D}",
                totalDeliveries = ledger.TotalCount,
                replayableCount = ledger.ReplayableCount,
                unauthorizedCount,
                failedCount,
                remediationGuidance = guidance,
                deliveries = filtered.Select(d => new
                {
                    d.Id,
                    d.DeliveryId,
                    d.Outcome,
                    d.HttpStatusCode,
                    d.AcceptedEventCount,
                    d.DeliveredCount,
                    d.CheckpointLsn,
                    d.ErrorCode,
                    d.ErrorSummary,
                    d.PayloadSizeBytes,
                    d.CanReplay,
                    d.ReplayCount,
                    d.ReceivedAtUtc,
                    d.ReplayedAtUtc,
                }),
            });
        }

        return new
        {
            inspectedStreamCount = reports.Count,
            totalReplayableDeadLetters = totalReplayable,
            totalUnauthorizedRejections = totalUnauthorized,
            totalFailedDeliveries = totalFailed,
            note = "Read-only diagnostics. Human approval and explicit replay API/UI invocation are required to replay dead-letters.",
            streams = reports,
        };
    }
}
