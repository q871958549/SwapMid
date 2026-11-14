using Volo.Abp.Application.Dtos;

namespace SwapMid.SystemManagement;

/// <summary>数据字典列表查询条件。</summary>
public class GetDataDictionaryListInput : PagedAndSortedResultRequestDto
{
    /// <summary>关键字过滤（字典编码 / 名称）。</summary>
    public string? Filter { get; set; }
    /// <summary>是否启用筛选。</summary>
    public bool? IsEnabled { get; set; }
}
