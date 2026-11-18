using System.Threading.Tasks;
using SwapMid.Notification;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace SwapMid.Controllers.Notification;

[RemoteService]
[Area("app")]
[Route("api/v1/notification/settings")]
public class NotificationSettingsController : SwapMidController, INotificationSettingsAppService
{
    private readonly INotificationSettingsAppService _service;

    public NotificationSettingsController(INotificationSettingsAppService service)
    {
        _service = service;
    }

    /// <summary>获取平台通知中心（邮件 SMTP / 通用 BotWebhook / Alertmanager）配置（密钥脱敏）。</summary>
    [HttpGet]
    public Task<NotificationSettingsDto> GetAsync() => _service.GetAsync();

    /// <summary>更新平台通知渠道参数与凭据配置。</summary>
    [HttpPut]
    public Task UpdateAsync(UpdateNotificationSettingsDto input) => _service.UpdateAsync(input);

    /// <summary>向指定邮件或 Webhook 渠道发送一条测试通知以验证配置连通性。</summary>
    [HttpPost("test")]
    public Task<NotificationTestResultDto> SendTestAsync(SendTestNotificationDto input) => _service.SendTestAsync(input);

    /// <summary>轮换并重新生成 Alertmanager 告警 Webhook 接入令牌。</summary>
    [HttpPost("alert-token")]
    public Task<string> RegenerateAlertTokenAsync() => _service.RegenerateAlertTokenAsync();
}
