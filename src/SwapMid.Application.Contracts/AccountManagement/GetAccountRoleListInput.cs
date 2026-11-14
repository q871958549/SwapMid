namespace SwapMid.AccountManagement;

/// <summary>角色列表查询条件。</summary>
public class GetAccountRoleListInput : TimeRangePagedAndSortedResultRequestDto
{
    /// <summary>关键字过滤。</summary>
    public string? Filter { get; set; }
}
