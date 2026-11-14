using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.Notification;

public interface INotificationSettingsAppService : IApplicationService
{
    Task<NotificationSettingsDto> GetAsync();
    Task UpdateAsync(UpdateNotificationSettingsDto input);
    Task<NotificationTestResultDto> SendTestAsync(SendTestNotificationDto input);
    Task<string> RegenerateAlertTokenAsync();
}

public class NotificationSettingsDto
{
    // ── Webhook 渠道（Slack / 飞书 / 钉钉 / 企业微信机器人 / 通用 JSON） ──
    public bool WebhookEnabled { get; set; }
    public string WebhookProvider { get; set; } = "generic";
    public string WebhookUrl { get; set; } = string.Empty;
    public bool WebhookSecretConfigured { get; set; }
    public string WebhookMentionUsers { get; set; } = string.Empty;

    // ── 邮件渠道（SMTP） ──
    public bool EmailEnabled { get; set; }
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 465;
    public bool SmtpUseSsl { get; set; } = true;
    public string SmtpUserName { get; set; } = string.Empty;
    public bool SmtpPasswordConfigured { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;

    // ── Alertmanager 告警接入 ──
    public bool AlertTokenConfigured { get; set; }
    public string AlertWebhookPath { get; set; } = "/api/v1/notification/webhooks/alertmanager";
}

public class UpdateNotificationSettingsDto
{
    public bool WebhookEnabled { get; set; }

    [StringLength(32)]
    public string? WebhookProvider { get; set; }

    [StringLength(1024)]
    public string? WebhookUrl { get; set; }

    /// <summary>Webhook 签名密钥：null 或空串 = 保持原密钥不变；传入 "__CLEAR__" 清空密钥。</summary>
    [StringLength(256)]
    public string? WebhookSecret { get; set; }

    [StringLength(256)]
    public string? WebhookMentionUsers { get; set; }

    public bool EmailEnabled { get; set; }

    [StringLength(256)]
    public string? SmtpHost { get; set; }

    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 465;

    public bool SmtpUseSsl { get; set; } = true;

    [StringLength(256)]
    public string? SmtpUserName { get; set; }

    [StringLength(256)]
    public string? SmtpPassword { get; set; }

    [StringLength(256)]
    public string? FromAddress { get; set; }

    [StringLength(128)]
    public string? FromName { get; set; }
}

public class SendTestNotificationDto
{
    public NotificationChannelType Channel { get; set; } = NotificationChannelType.Webhook;
    public NotificationLevel Level { get; set; } = NotificationLevel.Info;

    [StringLength(256)]
    public string? ToUser { get; set; }
}

public class NotificationTestResultDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
