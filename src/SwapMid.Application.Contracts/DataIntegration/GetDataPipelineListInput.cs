using System;

namespace SwapMid.DataIntegration;

/// <summary>数据管道列表查询入参</summary>
public class GetDataPipelineListInput : TimeRangePagedAndSortedResultRequestDto
{
    /// <summary>关键字过滤</summary>
    public string? Filter { get; set; }
    /// <summary>按源数据源 ID 过滤</summary>
    public Guid? SourceId { get; set; }
    /// <summary>按管道状态过滤</summary>
    public PipelineStatus? Status { get; set; }
}
