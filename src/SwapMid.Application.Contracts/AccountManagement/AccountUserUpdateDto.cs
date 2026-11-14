using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.AccountManagement;

/// <summary>账号编辑入参。</summary>
public class AccountUserUpdateDto
{
    /// <summary>用户名。</summary>
    [Required]
    [MaxLength(256)]
    public string UserName { get; set; } = null!;

    /// <summary>名。</summary>
    [MaxLength(64)]
    public string? Name { get; set; }

    /// <summary>姓。</summary>
    [MaxLength(64)]
    public string? Surname { get; set; }

    /// <summary>邮箱。</summary>
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = null!;

    /// <summary>手机号。</summary>
    [MaxLength(32)]
    public string? PhoneNumber { get; set; }

    /// <summary>是否启用。</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>绑定的员工 ID。</summary>
    public Guid? EmployeeId { get; set; }
    /// <summary>分配的角色名集合。</summary>
    public List<string> RoleNames { get; set; } = new();
}
