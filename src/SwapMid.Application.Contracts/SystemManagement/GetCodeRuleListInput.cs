using Volo.Abp.Application.Dtos;

namespace SwapMid.SystemManagement;

/// <summary>编码规则列表查询条件。</summary>
public class GetCodeRuleListInput : PagedAndSortedResultRequestDto
{
    /// <summary>关键字过滤。</summary>
    public string? Filter { get; set; }
    /// <summary>是否启用筛选。</summary>
    public bool? IsEnabled { get; set; }
}
