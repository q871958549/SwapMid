using System;
using System.Threading.Tasks;
using SwapMid.Notification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Volo.Abp;
using Volo.Abp.Auditing;

namespace SwapMid.Controllers.Notification;

/// <summary>
/// Prometheus Alertmanager 告警回调控制器。
/// 优先使用 <c>X-SwapMid-Alert-Token</c> 请求头传递令牌，同时兼容 <c>?token=</c> 查询参数。
/// </summary>
[RemoteService]
[Area("app")]
[Route("api/v1/notification/webhooks")]
[IgnoreAntiforgeryToken]
public class AlertWebhookController : SwapMidController
{
    public const string RateLimitPolicy = "AlertWebhook";
    public const int RetryAfterSeconds = 30;

    private readonly IAlertWebhookAppService _service;

    public AlertWebhookController(IAlertWebhookAppService service)
    {
        _service = service;
    }

    /// <summary>
    /// 接收 Prometheus Alertmanager 告警 Webhook 回调并转发至平台通知中心。
    /// </summary>
    [HttpPost("alertmanager")]
    [AllowAnonymous]
    [DisableAuditing]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<AlertWebhookReceiveResultDto> ReceiveAlertmanagerAsync(
        [FromQuery] string? token,
        [FromBody] AlertmanagerWebhookInput payload,
        [FromHeader(Name = "X-SwapMid-Alert-Token")] string? headerToken = null)
    {
        if (!string.IsNullOrEmpty(headerToken) &&
            !string.IsNullOrEmpty(token) &&
            !string.Equals(headerToken, token, StringComparison.Ordinal))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.NotificationCenter.InvalidWebhookToken);
        }

        return await _service.ReceiveAsync(headerToken ?? token, payload);
    }
}
