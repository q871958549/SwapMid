using System;

namespace SwapMid.AccountManagement;

/// <summary>账号列表查询条件。</summary>
public class GetAccountUserListInput : TimeRangePagedAndSortedResultRequestDto
{
    /// <summary>关键字过滤。</summary>
    public string? Filter { get; set; }
    /// <summary>是否启用筛选。</summary>
    public bool? IsActive { get; set; }

    /// <summary>按部门筛选账号（账号→员工反向关联：取该部门下员工绑定的账号）。</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>部门筛选时是否含下级部门（默认含）。</summary>
    public bool IncludeChildDepartments { get; set; } = true;

    /// <summary>按角色筛选账号（该角色的全部成员；服务端下推到 IdentityUser.Roles 导航集合查询，非内存过滤）。</summary>
    public Guid? RoleId { get; set; }
}
