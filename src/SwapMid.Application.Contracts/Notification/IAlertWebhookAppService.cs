using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.Notification;

/// <summary>
/// Prometheus Alertmanager 告警 Webhook 接收服务（匿名端点 + 恒定时间 Token 校验）。
/// </summary>
public interface IAlertWebhookAppService : IApplicationService
{
    Task<AlertWebhookReceiveResultDto> ReceiveAsync(string? token, AlertmanagerWebhookInput payload);
}

public class AlertWebhookReceiveResultDto
{
    public bool Accepted { get; set; }
    public int ReceivedCount { get; set; }
    public int FailedCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class AlertmanagerWebhookInput
{
    public string? Status { get; set; }
    public List<AlertmanagerAlertItemDto>? Alerts { get; set; }
}

public class AlertmanagerAlertItemDto
{
    public string? Status { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public Dictionary<string, string>? Annotations { get; set; }
}
