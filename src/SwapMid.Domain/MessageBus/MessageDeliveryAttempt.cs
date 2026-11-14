using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.MessageBus;

/// <summary>
/// 单条消息对<b>单个订阅</b>的投递明细(record × subscription 粒度)。
/// 引入它是为修「fan-out 顺序投递、record 级单状态」的缺陷A:多订阅时一家失败即整条重试、
/// 已成功订阅被重复投递。每个 attempt 独立状态 / 独立重试 / 独立死信;record.Status 退为聚合视图。
/// </summary>
public class MessageDeliveryAttempt : CreationAuditedEntity<Guid>
{
    /// <summary>所属消息记录 Id。</summary>
    public Guid RecordId { get; private set; }

    /// <summary>目标订阅 Id。</summary>
    public Guid SubscriptionId { get; private set; }

    /// <summary>订阅名称快照(订阅改名 / 删除后仍可读)。</summary>
    public string SubscriptionName { get; private set; } = null!;

    /// <summary>本订阅的投递状态(待投 / 已投 / 失败待重试 / 死信)。</summary>
    public MessageStatus Status { get; private set; }

    /// <summary>已重试次数。</summary>
    public int RetryCount { get; private set; }

    /// <summary>最近一次错误信息(可空)。</summary>
    public string? LastError { get; private set; }

    /// <summary>投递成功时间(可空)。</summary>
    public DateTime? DeliveredAt { get; private set; }

    private MessageDeliveryAttempt() { }

    public MessageDeliveryAttempt(Guid id, Guid recordId, Guid subscriptionId, string subscriptionName)
        : base(id)
    {
        RecordId = recordId;
        SubscriptionId = subscriptionId;
        SubscriptionName = subscriptionName;
        Status = MessageStatus.Pending;
        RetryCount = 0;
    }

    /// <summary>尚需投递(首投或失败待重试)。</summary>
    public bool IsPendingDelivery => Status is MessageStatus.Pending or MessageStatus.Failed;

    public void MarkDelivered()
    {
        Status = MessageStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;
        LastError = null;
    }

    /// <summary>一次失败:重试计数 +1,达上限则死信,否则失败待重试。</summary>
    public void MarkFailed(string? error, int maxAttempts)
    {
        RetryCount++;
        LastError = error;
        Status = RetryCount >= maxAttempts ? MessageStatus.DeadLettered : MessageStatus.Failed;
        DeliveredAt = null;
    }

    /// <summary>配置型错误(如投递类型不受支持):重试无意义,直接死信。</summary>
    public void MarkConfigDeadLettered(string? error)
    {
        Status = MessageStatus.DeadLettered;
        LastError = error;
        DeliveredAt = null;
    }

    /// <summary>手动重推:未成功的 attempt 归零重试;已成功的不动(不重复投递)。</summary>
    public void ResetForReplay()
    {
        if (Status == MessageStatus.Delivered)
        {
            return;
        }
        Status = MessageStatus.Pending;
        RetryCount = 0;
        LastError = null;
    }
}
