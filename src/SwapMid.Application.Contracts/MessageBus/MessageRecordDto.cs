using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.MessageBus;

/// <summary>消息记录</summary>
public class MessageRecordDto : EntityDto<Guid>
{
    /// <summary>所属主题 ID</summary>
    public Guid TopicId { get; set; }
    /// <summary>所属主题名称</summary>
    public string TopicName { get; set; } = null!;
    /// <summary>消息主题/标题</summary>
    public string Subject { get; set; } = null!;
    /// <summary>消息内容</summary>
    public string Payload { get; set; } = null!;
    /// <summary>消息状态</summary>
    public MessageStatus Status { get; set; }
    /// <summary>重试次数</summary>
    public int RetryCount { get; set; }
    /// <summary>投递成功时间</summary>
    public DateTime? DeliveredAt { get; set; }
    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }
}
