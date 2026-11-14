using System;

namespace SwapMid.MessageBus;

/// <summary>消息订阅列表查询入参</summary>
public class GetMessageSubscriptionListInput : TimeRangePagedAndSortedResultRequestDto
{
    /// <summary>按所属主题 ID 过滤</summary>
    public Guid? TopicId { get; set; }
    /// <summary>关键字过滤</summary>
    public string? Filter { get; set; }
}
