using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SimpleStateChecking;

namespace SwapMid.AccountManagement;

/// <summary>
/// 覆写 ABP 内置 <see cref="PermissionAppService"/>，统一拦截对 admin 角色的权限修改。
/// 拦截范围 = 基类的全部三个写方法：<see cref="UpdateAsync"/>(常规权限,含 ABP 原生端点
/// <c>PUT /api/permission-management/permissions?providerName=R&amp;providerKey=admin</c>、
/// <see cref="AccountRoleAppService.CopyAsync"/> 目标写入等一切调用路径)与
/// <see cref="UpdateResourceAsync"/>/<see cref="DeleteResourceAsync"/>(resource 级权限写端点
/// <c>PUT/DELETE /api/permission-management/permissions/resource</c>——平台当前未定义 resource 级权限,
/// 属前瞻性封堵,防未来引入后 R/admin 经此旁路)。
/// 收口 P0-7 遗留的已知残留旁路，详见 docs/account-roles-permissions-blueprint.md §2.4/§3.3 P1-5。
/// 门面 <see cref="AccountPermissionAppService"/> 的既有拦截保留(纵深防御 + 更快失败 + 与本类文案逐字一致)。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IPermissionAppService), typeof(PermissionAppService))]
public class AdminLockedPermissionAppService : PermissionAppService
{
    public AdminLockedPermissionAppService(
        IPermissionManager permissionManager,
        IPermissionChecker permissionChecker,
        IPermissionDefinitionManager permissionDefinitionManager,
        IResourcePermissionManager resourcePermissionManager,
        IResourcePermissionGrantRepository resourcePermissionGrantRepository,
        IOptions<PermissionManagementOptions> options,
        ISimpleStateCheckerManager<PermissionDefinition> simpleStateCheckerManager)
        : base(
            permissionManager,
            permissionChecker,
            permissionDefinitionManager,
            resourcePermissionManager,
            resourcePermissionGrantRepository,
            options,
            simpleStateCheckerManager)
    {
    }

    public override Task UpdateAsync(string providerName, string providerKey, UpdatePermissionsDto input)
    {
        if (AccountPermissionSharedConsts.IsAdminRoleUpdate(providerName, providerKey))
        {
            throw new UserFriendlyException(AccountPermissionSharedConsts.AdminRoleLockedMessage);
        }

        return base.UpdateAsync(providerName, providerKey, input);
    }

    // resource 级权限写端点(10.5 基类 virtual,provider 信息在 input 内)：同判定同文案拦 R/admin。
    // input 为 null 时不在此拦(与未覆写时行为一致,交由基类按原语义处理)。
    public override Task UpdateResourceAsync(string resourceName, string resourceKey, UpdateResourcePermissionsDto input)
    {
        if (input != null && AccountPermissionSharedConsts.IsAdminRoleUpdate(input.ProviderName, input.ProviderKey))
        {
            throw new UserFriendlyException(AccountPermissionSharedConsts.AdminRoleLockedMessage);
        }

        return base.UpdateResourceAsync(resourceName, resourceKey, input);
    }

    // resource 级权限删除端点(10.5 基类 virtual,provider 信息是方法参数)：同判定同文案拦 R/admin。
    public override Task DeleteResourceAsync(string resourceName, string resourceKey, string providerName, string providerKey)
    {
        if (AccountPermissionSharedConsts.IsAdminRoleUpdate(providerName, providerKey))
        {
            throw new UserFriendlyException(AccountPermissionSharedConsts.AdminRoleLockedMessage);
        }

        return base.DeleteResourceAsync(resourceName, resourceKey, providerName, providerKey);
    }
}
