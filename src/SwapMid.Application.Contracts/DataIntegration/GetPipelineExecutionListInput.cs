using System;

namespace SwapMid.DataIntegration;

/// <summary>管道执行历史列表查询入参</summary>
public class GetPipelineExecutionListInput : TimeRangePagedAndSortedResultRequestDto
{
    /// <summary>按所属管道 ID 过滤</summary>
    public Guid? PipelineId { get; set; }
    /// <summary>按执行状态过滤</summary>
    public ExecutionStatus? Status { get; set; }
    /// <summary>执行开始时间(起)</summary>
    public DateTime? StartedFrom { get; set; }
    /// <summary>执行开始时间(止)</summary>
    public DateTime? StartedTo { get; set; }
}
