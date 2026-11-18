using System;
using System.Collections.Generic;
using SwapMid.SharedKernel.Employees;

namespace SwapMid.AccountManagement;

/// <summary>
/// 角色分配弹窗专用的轻量账号行：只含勾选所需字段，不查员工/企业/部门绑定，
/// 角色名经「遍历角色取成员」批量映射，避免逐账号 N+1（1000 账号一次性拉取仍快）。
/// </summary>
public class AccountUserLiteDto
{
    /// <summary>账号唯一标识。</summary>
    public Guid Id { get; set; }
    /// <summary>用户名。</summary>
    public string UserName { get; set; } = null!;
    /// <summary>名。</summary>
    public string? Name { get; set; }
    /// <summary>所属角色名集合。</summary>
    public List<string> RoleNames { get; set; } = new();
}

/// <summary>账号管理页关联员工下拉专用在职员工选项（与员工主数据权限解耦）。</summary>
public class AccountEmployeeOptionDto
{
    /// <summary>员工档案 ID。</summary>
    public Guid Id { get; set; }
    /// <summary>已绑定账号 ID（未绑定为 null）。</summary>
    public Guid? UserId { get; set; }
    /// <summary>员工工号。</summary>
    public string EmployeeNo { get; set; } = null!;
    /// <summary>员工姓名。</summary>
    public string Name { get; set; } = null!;
    /// <summary>所属企业 ID。</summary>
    public Guid EnterpriseId { get; set; }
    /// <summary>所属企业名称。</summary>
    public string? EnterpriseName { get; set; }
    /// <summary>所属部门 ID。</summary>
    public Guid? DepartmentId { get; set; }
    /// <summary>所属部门名称。</summary>
    public string? DepartmentName { get; set; }
    /// <summary>员工邮箱。</summary>
    public string? Email { get; set; }
    /// <summary>员工手机号。</summary>
    public string? Phone { get; set; }
    /// <summary>岗位名称。</summary>
    public string? Position { get; set; }
    /// <summary>在职状态。</summary>
    public EmployeeStatus Status { get; set; }
}

/// <summary>账号管理页部门筛选下拉专用轻量部门选项（与部门主数据权限解耦）。</summary>
public class AccountDepartmentOptionDto
{
    /// <summary>部门 ID。</summary>
    public Guid Id { get; set; }
    /// <summary>部门名称。</summary>
    public string Name { get; set; } = null!;
    /// <summary>所属企业名称。</summary>
    public string? EnterpriseName { get; set; }
}
