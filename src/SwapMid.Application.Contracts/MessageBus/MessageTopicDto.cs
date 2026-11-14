using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.MessageBus;

/// <summary>消息主题</summary>
public class MessageTopicDto : FullAuditedEntityDto<Guid>
{
    /// <summary>主题名称</summary>
    public string Name { get; set; } = null!;
    /// <summary>主题描述</summary>
    public string? Description { get; set; }
    /// <summary>主题类型</summary>
    public TopicType Type { get; set; }
    /// <summary>重试次数</summary>
    public int RetryCount { get; set; }
    /// <summary>重试间隔(秒)</summary>
    public int RetryIntervalSeconds { get; set; }
    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }
}
