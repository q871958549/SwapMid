using System;

namespace SwapMid.ApiManagement;

/// <summary>API 路由列表查询入参</summary>
public class GetApiRouteListInput : TimeRangePagedAndSortedResultRequestDto
{
    /// <summary>关键字过滤</summary>
    public string? Filter { get; set; }
    /// <summary>按所属分组 ID 过滤</summary>
    public Guid? GroupId { get; set; }
    /// <summary>按是否启用过滤</summary>
    public bool? IsEnabled { get; set; }
}
