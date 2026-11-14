using System;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.MessageBus;

/// <summary>发布消息入参</summary>
public class PublishMessageDto
{
    /// <summary>目标主题 ID</summary>
    [Required] public Guid TopicId { get; set; }
    /// <summary>消息主题/标题</summary>
    [Required][MaxLength(256)] public string Subject { get; set; } = null!;
    /// <summary>消息内容</summary>
    [Required][MaxLength(8000)] public string Payload { get; set; } = null!;
}
