using System;

namespace SwapMid.MessageBus;

/// <summary>消息对单个订阅的投递明细(record × subscription 粒度),供记录详情页逐订阅展示投递结果。</summary>
public class MessageDeliveryAttemptDto
{
    public Guid Id { get; set; }
    /// <summary>目标订阅 Id。</summary>
    public Guid SubscriptionId { get; set; }
    /// <summary>订阅名称(投递时快照)。</summary>
    public string SubscriptionName { get; set; } = null!;
    /// <summary>该订阅的投递状态(待投 / 已投 / 失败待重试 / 死信)。</summary>
    public MessageStatus Status { get; set; }
    /// <summary>已重试次数。</summary>
    public int RetryCount { get; set; }
    /// <summary>最近一次错误信息(可空)。</summary>
    public string? LastError { get; set; }
    /// <summary>投递成功时间(可空)。</summary>
    public DateTime? DeliveredAt { get; set; }
    /// <summary>首次投递(明细创建)时间。</summary>
    public DateTime CreationTime { get; set; }
}
