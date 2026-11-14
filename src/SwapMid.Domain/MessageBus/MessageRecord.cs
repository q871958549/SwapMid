using System;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.MessageBus;

/// <summary>消息记录（一条已发布消息的投递流水，含内容、状态与重试轨迹）。</summary>
public class MessageRecord : CreationAuditedAggregateRoot<Guid>
{
    private const string AudienceSnapshotInitializedPropertyName = "MessageBus.AudienceSnapshotInitialized";

    /// <summary>所属主题 Id。</summary>
    public Guid TopicId { get; private set; }

    /// <summary>主题名称文本快照（发布时冗余留存，便于查询展示）。</summary>
    public string TopicName { get; private set; } = null!;

    /// <summary>消息标题 / 主题词。</summary>
    public string Subject { get; private set; } = null!;

    /// <summary>消息载荷（消息正文，通常为 JSON）。</summary>
    public string Payload { get; private set; } = null!;

    /// <summary>投递状态（待投递 / 已投递 / 失败 / 死信）。</summary>
    public MessageStatus Status { get; private set; }

    /// <summary>已重试次数。</summary>
    public int RetryCount { get; private set; }

    /// <summary>投递成功时间（可空）。</summary>
    public DateTime? DeliveredAt { get; private set; }

    /// <summary>错误信息（可空，失败 / 死信时记录）。</summary>
    public string? ErrorMessage { get; private set; }

    private MessageRecord() { }

    public MessageRecord(Guid id, Guid topicId, string topicName, string subject, string payload)
        : base(id)
    {
        TopicId = topicId;
        TopicName = topicName;
        Subject = subject;
        Payload = payload;
        Status = MessageStatus.Pending;
        RetryCount = 0;
    }

    public void MarkDelivered()
    {
        Status = MessageStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;
        ErrorMessage = null;
    }

    public void MarkFailed(string? errorMessage)
    {
        RetryCount++;
        ErrorMessage = errorMessage;
        Status = MessageStatus.Failed;
        DeliveredAt = null;
    }

    public void MarkDeadLettered(string? errorMessage)
    {
        Status = MessageStatus.DeadLettered;
        ErrorMessage = errorMessage;
        DeliveredAt = null;
    }

    public void ResetForRetry()
    {
        Status = MessageStatus.Pending;
        ErrorMessage = null;
        DeliveredAt = null;
    }

    /// <summary>
    /// 是否已经建立过订阅受众快照。该标记必须能表达“已初始化但零匹配订阅”，
    /// 否则 Replay 会把后来出现的订阅错误地纳入历史消息。
    /// </summary>
    public bool IsAudienceSnapshotInitialized =>
        this.GetProperty<bool>(AudienceSnapshotInitializedPropertyName);

    public void MarkAudienceSnapshotInitialized() =>
        this.SetProperty(AudienceSnapshotInitializedPropertyName, true);

    /// <summary>
    /// 按各订阅 <see cref="MessageDeliveryAttempt"/> 的明细状态聚合出记录整体状态(P2 缺陷A 修复后 record.Status 退为视图):
    /// 全部成功 → Delivered;尚有可重试(Pending/Failed)→ Failed(作业据此继续重投);其余(全终态且含死信)→ DeadLettered。
    /// <paramref name="retryCount"/> 取各 attempt 重试次数最大值;<paramref name="errorMessage"/> 取首个未成功 attempt 的错误。
    /// </summary>
    public void SetAggregateStatus(MessageStatus status, int retryCount, string? errorMessage)
    {
        Status = status;
        RetryCount = retryCount;
        if (status == MessageStatus.Delivered)
        {
            DeliveredAt ??= DateTime.UtcNow;
            ErrorMessage = null;
        }
        else
        {
            DeliveredAt = null;
            ErrorMessage = errorMessage;
        }
    }
}
