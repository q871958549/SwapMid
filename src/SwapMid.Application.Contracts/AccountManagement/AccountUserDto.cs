using System;
using System.Collections.Generic;
using Volo.Abp.Application.Dtos;

namespace SwapMid.AccountManagement;

/// <summary>账号信息（含绑定员工/企业/部门）。</summary>
public class AccountUserDto : FullAuditedEntityDto<Guid>
{
    /// <summary>用户名。</summary>
    public string UserName { get; set; } = null!;
    /// <summary>名。</summary>
    public string? Name { get; set; }
    /// <summary>姓。</summary>
    public string? Surname { get; set; }
    /// <summary>邮箱。</summary>
    public string Email { get; set; } = null!;
    /// <summary>手机号。</summary>
    public string? PhoneNumber { get; set; }
    /// <summary>是否启用。</summary>
    public bool IsActive { get; set; }
    /// <summary>所属角色名集合。</summary>
    public List<string> RoleNames { get; set; } = new();
    /// <summary>绑定的员工 ID。</summary>
    public Guid? BoundEmployeeId { get; set; }
    /// <summary>绑定的员工工号。</summary>
    public string? BoundEmployeeNo { get; set; }
    /// <summary>绑定的员工姓名。</summary>
    public string? BoundEmployeeName { get; set; }
    /// <summary>绑定的企业 ID。</summary>
    public Guid? BoundEnterpriseId { get; set; }
    /// <summary>绑定的企业名称。</summary>
    public string? BoundEnterpriseName { get; set; }
    /// <summary>绑定的部门 ID。</summary>
    public Guid? BoundDepartmentId { get; set; }
    /// <summary>绑定的部门名称。</summary>
    public string? BoundDepartmentName { get; set; }
}
