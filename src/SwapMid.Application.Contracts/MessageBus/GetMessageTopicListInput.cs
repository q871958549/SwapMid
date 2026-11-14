namespace SwapMid.MessageBus;

/// <summary>消息主题列表查询入参</summary>
public class GetMessageTopicListInput : TimeRangePagedAndSortedResultRequestDto
{
    /// <summary>关键字过滤</summary>
    public string? Filter { get; set; }
    /// <summary>按主题类型过滤</summary>
    public TopicType? Type { get; set; }
}
