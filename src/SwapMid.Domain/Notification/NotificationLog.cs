using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.Notification;

/// <summary>
/// 通知发送台账：每次经通用通知中心发送（每个渠道一条）落一条记录，供前端查历史与排查。
/// CreationTime = 发送时间；后台发送（Hangfire 失败通知等）无当前用户，CreatorId 可空。
/// </summary>
public class NotificationLog : CreationAuditedEntity<Guid>
{
    /// <summary>发送渠道（企业微信 / 邮件）。</summary>
    public NotificationChannelType Channel { get; private set; }

    /// <summary>通知标题 / 内容摘要。</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>接收人（该渠道下的目标地址：企微成员账号 / 邮箱）。</summary>
    public string? ToUser { get; private set; }

    /// <summary>通知级别（Info / Warning / Critical）。</summary>
    public NotificationLevel Level { get; private set; }

    /// <summary>来源标签（Alertmanager / Hangfire / 消息总线 / 数据集成 / 排产标准工时 / 通知中心·测试…）。</summary>
    public string? Source { get; private set; }

    /// <summary>是否发送成功。</summary>
    public bool Success { get; private set; }

    /// <summary>失败原因（发送成功时为空）。</summary>
    public string? Error { get; private set; }

    private NotificationLog()
    {
    }

    public NotificationLog(
        Guid id,
        NotificationChannelType channel,
        string title,
        string? toUser,
        NotificationLevel level,
        string? source,
        bool success,
        string? error)
        : base(id)
    {
        Channel = channel;
        Title = title;
        ToUser = toUser;
        Level = level;
        Source = source;
        Success = success;
        Error = error;
    }
}
