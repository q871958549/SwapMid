using System.Collections.Generic;

namespace SwapMid.AccountManagement;

/// <summary>权限项。</summary>
public class AccountPermissionDto
{
    /// <summary>权限名（唯一标识）。</summary>
    public string Name { get; set; } = null!;
    /// <summary>权限显示名称。</summary>
    public string DisplayName { get; set; } = null!;
    /// <summary>父权限名。</summary>
    public string? ParentName { get; set; }
    /// <summary>是否已授予。</summary>
    public bool IsGranted { get; set; }
    /// <summary>授予来源列表(仅"用户有效权限查询"场景填充,回答"他为什么有这权限"；
    /// 角色权限树等其余场景固定为空列表)。</summary>
    public List<AccountPermissionProviderDto> GrantedProviders { get; set; } = new();
}

/// <summary>权限授予来源(标注某项权限因何生效，如角色授予/用户直授)。</summary>
public class AccountPermissionProviderDto
{
    /// <summary>提供者名(R=角色授予、U=用户直授，ABP PermissionManagement 内置约定值)。</summary>
    public string ProviderName { get; set; } = null!;
    /// <summary>提供者键(随 ProviderName 语义而定：角色名 / 用户Id 等)。</summary>
    public string ProviderKey { get; set; } = null!;
}

/// <summary>权限分组（按业务域归类的一组权限）。</summary>
public class AccountPermissionGroupDto
{
    /// <summary>分组名（唯一标识）。</summary>
    public string Name { get; set; } = null!;
    /// <summary>分组显示名称。</summary>
    public string DisplayName { get; set; } = null!;
    /// <summary>该分组下的权限项列表。</summary>
    public List<AccountPermissionDto> Permissions { get; set; } = new();
}

/// <summary>角色权限视图（某角色已授予的全部权限，按分组组织）。</summary>
public class AccountRolePermissionDto
{
    /// <summary>角色名。</summary>
    public string RoleName { get; set; } = null!;
    /// <summary>权限分组列表。</summary>
    public List<AccountPermissionGroupDto> Groups { get; set; } = new();
}

/// <summary>单条权限授予变更项。</summary>
public class AccountPermissionGrantDto
{
    /// <summary>权限名（唯一标识）。</summary>
    public string Name { get; set; } = null!;
    /// <summary>是否授予。</summary>
    public bool IsGranted { get; set; }
}

/// <summary>角色权限更新入参（批量授予/撤销权限）。</summary>
public class AccountPermissionUpdateDto
{
    /// <summary>权限授予变更项集合。</summary>
    public List<AccountPermissionGrantDto> Permissions { get; set; } = new();
}
