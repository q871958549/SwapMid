using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.MessageBus;

/// <summary>消息主题（消息发布 / 订阅的逻辑通道，含投递失败的重试策略）。</summary>
public class MessageTopic : FullAuditedAggregateRoot<Guid>
{
    /// <summary>主题名称。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>主题描述（可空）。</summary>
    public string? Description { get; private set; }

    /// <summary>主题类型（队列：单播 / 广播：多订阅者）。</summary>
    public TopicType Type { get; private set; }

    /// <summary>投递失败最大重试次数。</summary>
    public int RetryCount { get; private set; }

    /// <summary>重试间隔（秒）。</summary>
    public int RetryIntervalSeconds { get; private set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; private set; }

    private MessageTopic() { }

    public MessageTopic(Guid id, string name, TopicType type, string? description = null,
        int retryCount = 3, int retryIntervalSeconds = 30)
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MessageTopicConsts.MaxNameLength);
        Type = type;
        Description = description;
        RetryCount = retryCount;
        RetryIntervalSeconds = retryIntervalSeconds;
        IsEnabled = true;
    }

    public void Update(string name, TopicType type, string? description, int retryCount, int retryIntervalSeconds)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MessageTopicConsts.MaxNameLength);
        Type = type;
        Description = description;
        RetryCount = retryCount;
        RetryIntervalSeconds = retryIntervalSeconds;
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
}
