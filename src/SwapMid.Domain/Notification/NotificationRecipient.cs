using System;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.Notification;

/// <summary>
/// 通知接收人（体系化收件人）。一人 = 多渠道地址（邮箱 / Webhook @提及标识）+ 订阅过滤（最低级别 / 来源）。
/// </summary>
public class NotificationRecipient : FullAuditedAggregateRoot<Guid>
{
    /// <summary>显示名 / 备注。</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>邮箱地址（邮件渠道用；空 = 该人不接收邮件）。</summary>
    public string? Email { get; private set; }

    /// <summary>Webhook 渠道 @提及标识（如用户 ID、手机号或 handle，可选）。</summary>
    public string? WebhookTarget { get; private set; }

    /// <summary>最低接收级别（Info=全收 / Warning / Critical=只收最高级）。</summary>
    public NotificationLevel MinLevel { get; private set; }

    /// <summary>订阅来源（逗号分隔，如 "Alertmanager,Hangfire"）；空 = 订阅全部来源。</summary>
    public string? Sources { get; private set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; private set; }

    private NotificationRecipient()
    {
    }

    public NotificationRecipient(
        Guid id,
        string name,
        string? email,
        string? webhookTarget,
        NotificationLevel minLevel,
        string? sources,
        bool isEnabled)
        : base(id)
    {
        SetInfo(name, email, webhookTarget, minLevel, sources, isEnabled);
    }

    public void Update(
        string name,
        string? email,
        string? webhookTarget,
        NotificationLevel minLevel,
        string? sources,
        bool isEnabled)
        => SetInfo(name, email, webhookTarget, minLevel, sources, isEnabled);

    private void SetInfo(
        string name,
        string? email,
        string? webhookTarget,
        NotificationLevel minLevel,
        string? sources,
        bool isEnabled)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), maxLength: 64);
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        WebhookTarget = string.IsNullOrWhiteSpace(webhookTarget) ? null : webhookTarget.Trim();
        MinLevel = minLevel;
        Sources = string.IsNullOrWhiteSpace(sources) ? null : sources.Trim();
        IsEnabled = isEnabled;
    }

    /// <summary>该接收人是否匹配某条通知（启用 + 级别达标 + 来源订阅）。</summary>
    public bool Matches(NotificationLevel level, string? source)
    {
        if (!IsEnabled)
            return false;
        if (level < MinLevel)
            return false;
        if (string.IsNullOrWhiteSpace(Sources))
            return true;
        if (string.IsNullOrWhiteSpace(source))
            return true;
        return Sources
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(s => string.Equals(s, source, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>取指定渠道对应的接收地址；该人未配该渠道地址时返回 null。</summary>
    public string? AddressFor(NotificationChannelType channel) => channel switch
    {
        NotificationChannelType.Email => Email,
        NotificationChannelType.Webhook => WebhookTarget,
        _ => null
    };
}
