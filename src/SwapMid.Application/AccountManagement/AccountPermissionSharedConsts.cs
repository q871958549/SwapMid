using System;

namespace SwapMid.AccountManagement;

/// <summary>
/// 角色权限管理跨类共享的常量与判定逻辑。
/// admin 锁定判定+文案被 <see cref="AccountPermissionAppService"/>(平台门面,快速失败)与
/// <see cref="AdminLockedPermissionAppService"/>(覆写 ABP 原生 <c>IPermissionAppService</c>,收口其余通道，
/// 见 P1-5/docs/account-roles-permissions-blueprint.md §2.4 已知残留旁路)两处引用，避免文案/判定逻辑漂移。
/// 权限变更审计评论前缀被 <see cref="AccountPermissionAppService.UpdateRolePermissionsAsync"/>(写)与
/// <see cref="AccountRoleAppService.GetPermissionChangesAsync"/>(读)两处引用。
/// </summary>
internal static class AccountPermissionSharedConsts
{
    /// <summary>角色级权限授权的 ProviderName 约定值(ABP PermissionManagement 内置)。</summary>
    public const string RoleProviderName = "R";

    /// <summary>ABP 默认超管角色名(isStatic)。恒全权由内置 PermissionDataSeedContributor + DbMigrator 保证。</summary>
    public const string AdminRoleName = "admin";

    /// <summary>admin 角色权限锁定提示文案(两处拦截逐字一致)。</summary>
    public const string AdminRoleLockedMessage = "系统管理员角色恒有全部权限(由种子机制保证,新增权限随 DbMigrator 自动授予),不可在此修改";

    /// <summary>角色权限变更审计评论的固定前缀，便于按 AuditLog.Comments 精确检索本类变更行
    /// (格式：<c>[RolePermissionChange] {roleName} +[a,b] -[c]</c>，+/- 段无内容时省略该段)。</summary>
    public const string ChangeCommentPrefix = "[RolePermissionChange] ";

    /// <summary>目标角色名是否为 admin(大小写不敏感)。</summary>
    public static bool IsAdminRole(string roleName)
        => string.Equals(roleName, AdminRoleName, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否命中"角色级 provider 且目标是 admin 角色"——覆写类 <see cref="AdminLockedPermissionAppService"/>
    /// 用此判定拦截全部通道(ABP 原生端点/CopyAsync 目标写入等，provider 名精确匹配用 Ordinal，角色名大小写不敏感)。</summary>
    public static bool IsAdminRoleUpdate(string providerName, string providerKey)
        => string.Equals(providerName, RoleProviderName, StringComparison.Ordinal) && IsAdminRole(providerKey);
}
