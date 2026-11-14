using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.PermissionManagement;
using Xunit;

namespace SwapMid.AccountManagement;

/// <summary>
/// P1-5:验证 <see cref="AdminLockedPermissionAppService"/> 覆写 ABP 内置 <c>IPermissionAppService</c> 生效，
/// 一处拦全部通道(见 docs/account-roles-permissions-blueprint.md §3.3 P1-5)。
/// </summary>
[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
public class AdminLockedPermissionAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IPermissionAppService _permissionAppService;
    private readonly IAccountRoleAppService _roleAppService;

    public AdminLockedPermissionAppServiceTests()
    {
        _permissionAppService = GetRequiredService<IPermissionAppService>();
        _roleAppService = GetRequiredService<IAccountRoleAppService>();
    }

    // 覆写生效性:解析 IPermissionAppService 断言拿到的是本平台覆写类型，防 [ExposeServices]/[Dependency] 漏配
    // 静默退回 ABP 原生实现(本仓 messagebus 曾踩同型坑，见记忆)。
    // ⚠️ ABP+Autofac 对 ApplicationService(IUnitOfWorkEnabled/IValidationEnabled 等标记接口触发)做接口级动态代理
    // 包装——解析到的是 Castle.Proxies.IApplicationServiceProxy(只实现接口、不继承具体类)，直接
    // ShouldBeAssignableTo<AdminLockedPermissionAppService> 恒失败(与覆写是否生效无关，是代理机制本身)。
    // 须经 IProxyTargetAccessor.DynProxyGetTarget() 拿到代理背后的真实实例再判类型。
    [Fact]
    public void Should_Resolve_AdminLockedPermissionAppService_For_IPermissionAppService()
    {
        var target = (_permissionAppService as IProxyTargetAccessor)?.DynProxyGetTarget()
            ?? _permissionAppService;

        target.ShouldBeAssignableTo<AdminLockedPermissionAppService>();
    }

    // 覆写拦截 admin 角色:直接调用底层 IPermissionAppService(绕过平台门面 AccountPermissionAppService)，
    // 验证 ABP 原生端点等其余通道同样被拦——判定发生在 base.UpdateAsync 之前，不依赖 DB 中是否真有 admin 角色行。
    [Fact]
    public async Task UpdateAsync_Should_Throw_When_Target_Is_Admin_Role()
    {
        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _permissionAppService.UpdateAsync("R", "admin", new UpdatePermissionsDto
            {
                Permissions = Array.Empty<UpdatePermissionDto>()
            }));

        exception.Message.ShouldBe("系统管理员角色恒有全部权限(由种子机制保证,新增权限随 DbMigrator 自动授予),不可在此修改");
    }

    // 普通角色不受影响:走 base.UpdateAsync 正常返回，不抛异常(覆写只精确拦 R+admin，不误伤其余角色/provider)。
    [Fact]
    public async Task UpdateAsync_Should_Not_Throw_For_Ordinary_Role()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = $"lock-test-role-{suffix}";
        await _roleAppService.CreateAsync(new AccountRoleCreateUpdateDto { Name = roleName, IsPublic = true });

        await Should.NotThrowAsync(() =>
            _permissionAppService.UpdateAsync("R", roleName, new UpdatePermissionsDto
            {
                Permissions = Array.Empty<UpdatePermissionDto>()
            }));
    }

    // resource 级权限写端点同样被拦(provider 信息在 input 内；平台当前无 resource 级权限定义,
    // 属前瞻性封堵)。判定发生在 base 之前，顺带覆盖 providerKey 大小写不敏感。
    [Fact]
    public async Task UpdateResourceAsync_Should_Throw_When_Provider_Is_Admin_Role()
    {
        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _permissionAppService.UpdateResourceAsync("AnyResource", "any-key", new UpdateResourcePermissionsDto
            {
                ProviderName = "R",
                ProviderKey = "ADMIN",
                Permissions = new List<string>()
            }));

        exception.Message.ShouldBe("系统管理员角色恒有全部权限(由种子机制保证,新增权限随 DbMigrator 自动授予),不可在此修改");
    }

    // resource 级权限删除端点同样被拦(provider 信息是方法参数)。
    [Fact]
    public async Task DeleteResourceAsync_Should_Throw_When_Provider_Is_Admin_Role()
    {
        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _permissionAppService.DeleteResourceAsync("AnyResource", "any-key", "R", "admin"));

        exception.Message.ShouldBe("系统管理员角色恒有全部权限(由种子机制保证,新增权限随 DbMigrator 自动授予),不可在此修改");
    }
}
