using System;

namespace SwapMid.ApiManagement;

/// <summary>API 分组列表查询入参</summary>
public class GetApiGroupListInput : TimeRangePagedAndSortedResultRequestDto
{
    /// <summary>关键字过滤</summary>
    public string? Filter { get; set; }
}
