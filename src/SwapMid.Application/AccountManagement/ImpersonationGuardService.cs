using System;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace SwapMid.AccountManagement;

/// <summary>
/// 模拟登录（管理员以目标账号身份登录）的权限边界判定——四条防护规则的唯一落点，
/// 供 <c>ImpersonationTokenExtensionGrant</c>（HttpApi.Host/OpenIddict，OpenIddict extension grant
/// 管线）调用。抽成独立可注入的 Application 层服务而非直接写进 grant 类，是为了让规则脱离
/// OpenIddict 管线单独可测（见 docs/account-impersonation-blueprint.md §3.2）。
///
/// ⚠️ 安全边界：本服务只做"已认证身份上的业务规则判定"，不做 token 有效性校验——调用方必须先经
/// OpenIddict introspection 校验 <paramref name="callerPrincipal"/> 的有效性（grant 内已做）。
/// 前端按钮显隐只是 UX，真正的权限边界在这里 + <see cref="IPermissionChecker"/>。
/// </summary>
public class ImpersonationGuardService : ITransientDependency
{
    // admin 角色名字面量：与 ABP 内置种子(IdentityDataSeeder 建角色 / PermissionDataSeedContributor 授权)的 admin 角色名保持一致
    // （ABP 标准种子角色名）。全仓未对此导出共享常量，两处各自持有同一字面量属现状既有约定。
    private const string AdminRoleName = "admin";

    private readonly IPermissionChecker _permissionChecker;
    private readonly IImpersonationIdentityLookup _users;

    public ImpersonationGuardService(IPermissionChecker permissionChecker, IdentityUserManager userManager)
        : this(permissionChecker, new IdentityUserManagerImpersonationLookup(userManager))
    {
    }

    internal ImpersonationGuardService(IPermissionChecker permissionChecker, IImpersonationIdentityLookup users)
    {
        _permissionChecker = permissionChecker;
        _users = users;
    }

    /// <summary>
    /// 校验"<paramref name="callerPrincipal"/> 是否可模拟 <paramref name="targetUserId"/>"。
    /// 规则顺序判定、一票否决：①禁链式 ②须持 Impersonate 权限 ③禁模拟自己 ④目标须存在且 IsActive
    /// ⑤非 admin 不得模拟 admin。④⑤的拒绝原因统一归为 <see cref="ImpersonationDenyReason.TargetUnavailable"/>，
    /// 防止调用方据此反推"目标是否存在 / 是否 admin"（蓝图 §3.4 安全红线⑥）。
    /// </summary>
    public async Task<ImpersonationGuardResult> ValidateAsync(ClaimsPrincipal callerPrincipal, Guid targetUserId)
    {
        // ①禁链式：caller 自身的 token 已带 ImpersonatorUserId claim，说明其正处于被模拟状态。
        if (callerPrincipal.FindImpersonatorUserId() != null)
        {
            return ImpersonationGuardResult.Deny(ImpersonationDenyReason.AlreadyImpersonating);
        }

        var callerId = callerPrincipal.FindUserId();
        if (callerId == null)
        {
            return ImpersonationGuardResult.Deny(ImpersonationDenyReason.InvalidCaller);
        }

        // ②权限：对 caller 的 principal（而非 ambient 当前用户）查——OpenIddict grant 管线里没有
        // ICurrentUser 上下文，只能显式传入 introspection 校验出的 principal。
        if (!await _permissionChecker.IsGrantedAsync(callerPrincipal, SwapMidPermissions.AccountManagement.Users.Impersonate))
        {
            return ImpersonationGuardResult.Deny(ImpersonationDenyReason.PermissionDenied);
        }

        // ③禁模拟自己
        if (targetUserId == callerId.Value)
        {
            return ImpersonationGuardResult.Deny(ImpersonationDenyReason.SelfImpersonation);
        }

        var callerUser = await _users.FindAsync(callerId.Value);
        if (callerUser == null)
        {
            return ImpersonationGuardResult.Deny(ImpersonationDenyReason.InvalidCaller);
        }

        // ④目标须存在且 IsActive——不存在与已停用统一判同一拒绝原因。
        var targetUser = await _users.FindAsync(targetUserId);
        if (targetUser == null || !targetUser.IsActive)
        {
            return ImpersonationGuardResult.Deny(ImpersonationDenyReason.TargetUnavailable);
        }

        // ⑤非 admin 不得模拟 admin（防未来权限下放后横向提权：Impersonate 权限一旦下放给
        // 非 admin 角色，仍不能借此拿到 admin 身份）。
        if (await _users.IsInRoleAsync(targetUser, AdminRoleName)
            && !await _users.IsInRoleAsync(callerUser, AdminRoleName))
        {
            return ImpersonationGuardResult.Deny(ImpersonationDenyReason.TargetUnavailable);
        }

        return ImpersonationGuardResult.Allow(callerUser, targetUser);
    }
}

/// <summary>模拟登录拒绝原因——仅供 grant 内部按语义映射标准 OAuth error/结构化日志，不对外暴露原始枚举值。</summary>
public enum ImpersonationDenyReason
{
    None = 0,

    /// <summary>caller 身份无法解析（principal 缺 UserId claim / 账号已不存在）。</summary>
    InvalidCaller,

    /// <summary>禁链式：caller 自身正处于被模拟状态。</summary>
    AlreadyImpersonating,

    /// <summary>caller 不持有 Impersonate 权限。</summary>
    PermissionDenied,

    /// <summary>目标账号等于 caller 自己。</summary>
    SelfImpersonation,

    /// <summary>目标不可用的统一归类：不存在 / 已停用 / 非 admin 试图模拟 admin，三者对外一律同一拒绝原因，防探测。</summary>
    TargetUnavailable,
}

/// <summary>模拟登录校验结果。<see cref="Caller"/>/<see cref="Target"/> 仅在 <see cref="IsAllowed"/> 为真时非空。</summary>
public class ImpersonationGuardResult
{
    public bool IsAllowed { get; }
    public ImpersonationDenyReason DenyReason { get; }
    public IdentityUser? Caller { get; }
    public IdentityUser? Target { get; }

    private ImpersonationGuardResult(bool isAllowed, ImpersonationDenyReason denyReason, IdentityUser? caller, IdentityUser? target)
    {
        IsAllowed = isAllowed;
        DenyReason = denyReason;
        Caller = caller;
        Target = target;
    }

    public static ImpersonationGuardResult Allow(IdentityUser caller, IdentityUser target)
        => new(true, ImpersonationDenyReason.None, caller, target);

    public static ImpersonationGuardResult Deny(ImpersonationDenyReason reason)
        => new(false, reason, null, null);
}

/// <summary>把 <see cref="IdentityUserManager"/> 从规则里隔开，便于单测五条否决而不代理 ABP 具体类。</summary>
internal interface IImpersonationIdentityLookup
{
    Task<IdentityUser?> FindAsync(Guid id);
    Task<bool> IsInRoleAsync(IdentityUser user, string roleName);
}

internal sealed class IdentityUserManagerImpersonationLookup : IImpersonationIdentityLookup
{
    private readonly IdentityUserManager _userManager;

    public IdentityUserManagerImpersonationLookup(IdentityUserManager userManager)
    {
        _userManager = userManager;
    }

    public Task<IdentityUser?> FindAsync(Guid id) => _userManager.FindByIdAsync(id.ToString());

    public Task<bool> IsInRoleAsync(IdentityUser user, string roleName)
        => _userManager.IsInRoleAsync(user, roleName);
}
