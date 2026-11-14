using System;
using System.Collections.Generic;
using System.Security.Claims;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Roles;
using Volo.Abp.Security.Claims;

namespace SwapMid.Security;

[Dependency(ReplaceServices = true)]
public class FakeCurrentPrincipalAccessor : ThreadCurrentPrincipalAccessor
{
    protected override ClaimsPrincipal GetClaimsPrincipal()
    {
        return GetPrincipal();
    }

    private ClaimsPrincipal GetPrincipal()
    {
        return new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
        {
            new Claim(AbpClaimTypes.UserId, "2e701e62-0953-4dd3-910b-dc6cc93ccb0d"),
            new Claim(AbpClaimTypes.UserName, "admin"),
            new Claim(AbpClaimTypes.Email, "admin@abp.io"),
            // ABP 10.5 起 IdentityUserAppService.UpdateUserByInput 新增 FilterRolesByCurrentUserAsync 校验：
            // 非 admin 调用者会走 UserManager.GetByIdAsync(CurrentUser.GetId()) 反查“操作者”实体核对可授权角色集合，
            // 而本 fake principal 的 UserId 在测试库里没有对应真实行，会抛 EntityNotFoundException。
            // 之前的 principal 缺 Role 声明，HasAdminRoleAsync()（纯声明判断，不查库）恒为 false，才会走到这条新路径。
            // 补上 admin 角色声明使其与真实 admin 登录态一致（短路该新校验），非绕过安全检查——
            // 已用真机 API 验证：真实已登录 admin 创建带角色账号完全正常，本改动只是让 fake principal 更贴近真实态。
            new Claim(AbpClaimTypes.Role, AbpRoleConsts.AdminRoleName)
        }));
    }
}
