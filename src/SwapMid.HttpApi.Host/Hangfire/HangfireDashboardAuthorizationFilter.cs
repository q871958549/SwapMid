using Hangfire.Dashboard;
using SwapMid.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Authorization.Permissions;

namespace SwapMid.Hangfire;

/// <summary>
/// Hangfire Dashboard 访问拦截：
/// - 开发环境放开，便于本地巡检；
/// - 生产环境要求已登录，且满足以下任一条件：① 属于 admin 角色；② 被授予
///   <see cref="SwapMidPermissions.SystemManagement.HangfireDashboard"/> 权限。
/// 说明：本 Host 同时是 ABP MVC + Account 宿主，浏览器访问 /hangfire 需先经 /Account/Login
/// 拿到 Cookie 身份（Next 前端走的是 OpenIddict Token，不会带这个 Cookie）。
/// </summary>
public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly bool _allowAnonymousInDevelopment;
    private readonly string _adminRoleName;

    public HangfireDashboardAuthorizationFilter(bool allowAnonymousInDevelopment, string adminRoleName = "admin")
    {
        _allowAnonymousInDevelopment = allowAnonymousInDevelopment;
        _adminRoleName = adminRoleName;
    }

    public bool Authorize(DashboardContext context)
    {
        if (_allowAnonymousInDevelopment)
        {
            return true;
        }

        var httpContext = context.GetHttpContext();
        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        // admin 角色直接放行（种子角色，必然拥有）。
        if (user.IsInRole(_adminRoleName))
        {
            return true;
        }

        // 否则校验独立的 Hangfire 看板权限（可在权限管理里授予非 admin 角色）。
        var permissionChecker = httpContext.RequestServices.GetService<IPermissionChecker>();
        if (permissionChecker == null)
        {
            return false;
        }

        return permissionChecker
            .IsGrantedAsync(SwapMidPermissions.SystemManagement.HangfireDashboard.Default)
            .GetAwaiter()
            .GetResult();
    }
}
