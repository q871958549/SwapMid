using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SwapMid.Controllers.Notification;
using SwapMid.DataIntegration;
using SwapMid.Permissions;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Uow;

namespace SwapMid.Controllers.DataIntegration;

/// <summary>
/// Serverless 认证 Push Webhook CDC 事件推送接入端点：
/// 强制校验 <c>X-SwapMid-Signature: sha256=&lt;hex&gt;</c>（HMAC-SHA256）与 2 MiB 请求体上限，
/// 将外部系统直推变更事件投递至 CDC 全下游目标（含 Delta Lake / Apache Iceberg v2）。
/// </summary>
[RemoteService]
[Area("app")]
[Route("api/v1/cdc-webhooks")]
[IgnoreAntiforgeryToken]
public class CdcWebhookIngressController : SwapMidController
{
    private const int MaxPayloadBytes = 2 * 1024 * 1024;
    private readonly ICdcWebhookIngressService _ingressService;

    public CdcWebhookIngressController(ICdcWebhookIngressService ingressService)
    {
        _ingressService = ingressService;
    }

    /// <summary>接收 HMAC-SHA256 签名的 Push Webhook CDC 变更事件批次并投递至下游目标</summary>
    [HttpPost("{streamId:guid}")]
    [AllowAnonymous]
    [DisableAuditing]
    [UnitOfWork(IsDisabled = true)]
    [EnableRateLimiting(AlertWebhookController.RateLimitPolicy)]
    [ProducesResponseType(typeof(CdcWebhookIngestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> IngestAsync(
        Guid streamId,
        [FromHeader(Name = "X-SwapMid-Signature")] string? signature = null,
        [FromHeader(Name = "X-SwapMid-Delivery-Id")] string? deliveryId = null,
        CancellationToken cancellationToken = default)
    {
        if (Request.ContentLength is long contentLength && contentLength > MaxPayloadBytes)
        {
            _ingressService.RecordPayloadTooLarge();
            return StatusCode(StatusCodes.Status413PayloadTooLarge, BuildError("SwapMid:ETL:PayloadTooLarge", "Webhook 请求体超过 2 MiB 上限。"));
        }

        byte[] rawBodyBytes;
        using (var buffer = new MemoryStream())
        {
            var chunk = new byte[16 * 1024];
            var total = 0;
            while (true)
            {
                var read = await Request.Body.ReadAsync(chunk, cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > MaxPayloadBytes)
                {
                    _ingressService.RecordPayloadTooLarge();
                    return StatusCode(StatusCodes.Status413PayloadTooLarge, BuildError("SwapMid:ETL:PayloadTooLarge", "Webhook 请求体超过 2 MiB 上限。"));
                }

                buffer.Write(chunk, 0, read);
            }

            rawBodyBytes = buffer.ToArray();
        }

        var outcome = await _ingressService.IngestAsync(
            streamId,
            rawBodyBytes,
            signature,
            deliveryId,
            cancellationToken);

        return outcome.Outcome switch
        {
            CdcWebhookIngestOutcome.Accepted => Ok(outcome.Result),
            CdcWebhookIngestOutcome.Unauthorized => Unauthorized(BuildError(outcome.ErrorCode, outcome.ErrorMessage)),
            CdcWebhookIngestOutcome.NotFound => NotFound(BuildError(outcome.ErrorCode, outcome.ErrorMessage)),
            CdcWebhookIngestOutcome.Inactive => Conflict(BuildError(outcome.ErrorCode, outcome.ErrorMessage)),
            CdcWebhookIngestOutcome.PayloadTooLarge => StatusCode(StatusCodes.Status413PayloadTooLarge, BuildError(outcome.ErrorCode, outcome.ErrorMessage)),
            CdcWebhookIngestOutcome.Busy => StatusCode(StatusCodes.Status429TooManyRequests, BuildError(outcome.ErrorCode, outcome.ErrorMessage)),
            _ => BadRequest(BuildError(outcome.ErrorCode, outcome.ErrorMessage)),
        };
    }

    /// <summary>查询 Push Webhook CDC 流最近的认证推送、拒签与死信待重放台账</summary>
    [HttpGet("{streamId:guid}/deliveries")]
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Default)]
    public Task<CdcWebhookDeliveryListDto> GetDeliveriesAsync(
        Guid streamId,
        [FromQuery] int maxCount = 30,
        CancellationToken cancellationToken = default)
        => _ingressService.GetDeliveriesAsync(streamId, maxCount, cancellationToken);

    /// <summary>重放一条已通过验签但因流未激活或下游故障留存的 Push Webhook 死信批次</summary>
    [HttpPost("{streamId:guid}/deliveries/{deliveryId:guid}/replay")]
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [UnitOfWork(IsDisabled = true)]
    public Task<CdcWebhookIngestResultDto> ReplayDeliveryAsync(
        Guid streamId,
        Guid deliveryId,
        CancellationToken cancellationToken = default)
        => _ingressService.ReplayDeliveryAsync(streamId, deliveryId, cancellationToken);

    private static object BuildError(string code, string message)
        => new { error = new { code, message } };
}
