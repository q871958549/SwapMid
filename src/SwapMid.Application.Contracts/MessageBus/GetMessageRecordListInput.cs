using System;

namespace SwapMid.MessageBus;

/// <summary>消息记录列表查询入参</summary>
public class GetMessageRecordListInput : TimeRangePagedAndSortedResultRequestDto
{
    /// <summary>按所属主题 ID 过滤</summary>
    public Guid? TopicId { get; set; }
    /// <summary>按消息状态过滤</summary>
    public MessageStatus? Status { get; set; }
    /// <summary>关键字过滤</summary>
    public string? Filter { get; set; }
}
