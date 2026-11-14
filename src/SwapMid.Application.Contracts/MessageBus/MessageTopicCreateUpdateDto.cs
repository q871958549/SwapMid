using System.ComponentModel.DataAnnotations;

namespace SwapMid.MessageBus;

/// <summary>消息主题新增/编辑入参</summary>
public class MessageTopicCreateUpdateDto
{
    /// <summary>主题名称</summary>
    [Required][MaxLength(128)] public string Name { get; set; } = null!;
    /// <summary>主题描述</summary>
    [MaxLength(512)] public string? Description { get; set; }
    /// <summary>主题类型</summary>
    [Required] public TopicType Type { get; set; }
    /// <summary>重试次数</summary>
    public int RetryCount { get; set; } = 3;
    /// <summary>重试间隔(秒)</summary>
    public int RetryIntervalSeconds { get; set; } = 30;
    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}
