using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.AccountManagement;

/// <summary>角色信息。</summary>
public class AccountRoleDto : FullAuditedEntityDto<Guid>
{
    /// <summary>角色名称。</summary>
    public string Name { get; set; } = null!;
    /// <summary>是否为默认角色（新用户自动分配）。</summary>
    public bool IsDefault { get; set; }
    /// <summary>是否为内置角色（系统预置，不可删除）。</summary>
    public bool IsStatic { get; set; }
    /// <summary>是否公开角色。</summary>
    public bool IsPublic { get; set; }
    /// <summary>角色描述（走 ExtraProperties 存储，零迁移）。</summary>
    public string? Description { get; set; }
    /// <summary>该角色当前成员数。</summary>
    public int MemberCount { get; set; }
    /// <summary>该角色当前已授予的权限数。</summary>
    public int PermissionCount { get; set; }
    /// <summary>并发戳（乐观并发，PUT 时需回传）。</summary>
    public string? ConcurrencyStamp { get; set; }
}
