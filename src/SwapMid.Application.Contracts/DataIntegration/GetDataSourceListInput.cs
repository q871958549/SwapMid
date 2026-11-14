namespace SwapMid.DataIntegration;

/// <summary>数据源列表查询入参</summary>
public class GetDataSourceListInput : TimeRangePagedAndSortedResultRequestDto
{
    /// <summary>关键字过滤</summary>
    public string? Filter { get; set; }
    /// <summary>按数据源类型过滤</summary>
    public DataSourceType? Type { get; set; }
}
