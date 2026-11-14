namespace SwapMid.Notification;

/// <summary>
/// 通用通知中心 —— 通知渠道类型。
/// </summary>
public enum NotificationChannelType
{
    /// <summary>通用群机器人 / HTTP Webhook（Slack / 飞书 / 钉钉 / 企业微信机器人 / 自定义 JSON）。</summary>
    Webhook = 1,

    /// <summary>邮件（SMTP）。</summary>
    Email = 2
}

/// <summary>通知级别。</summary>
public enum NotificationLevel
{
    Info = 0,
    Warning = 1,
    Critical = 2
}
