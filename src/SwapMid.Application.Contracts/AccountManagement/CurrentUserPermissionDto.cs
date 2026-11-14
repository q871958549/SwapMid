using System;
using System.Collections.Generic;

namespace SwapMid.AccountManagement;

/// <summary>当前登录用户的权限集合。</summary>
public class CurrentUserPermissionDto
{
    /// <summary>已授予的权限名集合。</summary>
    public List<string> GrantedPermissions { get; set; } = new();
}

/// <summary>指定用户当前生效的权限视图(按分组组织,每项权限附授予来源——回答"他为什么有这权限"；
/// 与角色权限树共用 <see cref="AccountPermissionGroupDto"/>/<see cref="AccountPermissionDto"/> 形状，
/// 前端可复用同一套树形展示/剥前缀逻辑)。</summary>
public class AccountUserPermissionDto
{
    /// <summary>用户 Id。</summary>
    public Guid UserId { get; set; }
    /// <summary>权限分组列表。</summary>
    public List<AccountPermissionGroupDto> Groups { get; set; } = new();
}
