using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using SwapMid.AccountManagement;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using Volo.Abp.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using SignInResult = Microsoft.AspNetCore.Mvc.SignInResult;

namespace SwapMid.OpenIddict;

/// <summary>
/// 管理员模拟登录（Impersonation）extension grant——账号页「以此账号登录」的服务端落点
/// （ABP 开源官方扩展点 <see cref="ITokenExtensionGrant"/>）。种子里 <c>SwapMid_App</c>
/// 客户端 grantTypes 已含 <c>"Impersonation"</c>（<c>OpenIddictDataSeedContributor</c>），本类是其实现。
///
/// 安全红线（评审重点，逐条对应 docs/account-impersonation-blueprint.md §3.4）：
/// ① 权限边界在本类 + <see cref="ImpersonationGuardService"/> 内部（PermissionChecker + 四条防护规则），
///    前端按钮显隐只是 UX，缺一不可；
/// ② 模拟 token 绝不携带 refresh token / offline_access——下方强制从请求 scope 剔除，不信任客户端传参；
/// ③ 原会话（管理员身份）快照只由前端 BFF 会话层持有（<c>web-react/src/lib/session.ts</c>），
///    本类完全不涉及、不接触；
/// ④ 每个拒绝分支统一返回 invalid_grant/invalid_request + 模糊文案，不泄漏目标账号是否存在/是否 admin。
///
/// ⚠️ 实现坑（见蓝图 §5）：grant 实例由 <c>AbpOpenIddictExtensionGrantsOptions.Grants.Add(name, new ...())</c>
/// 以裸 <c>new</c> 注册（非 DI 容器实例化），故本类刻意保持无参构造、所有依赖一律从
/// <c>context.HttpContext.RequestServices</c> 现取，不做构造函数注入。
/// </summary>
public class ImpersonationTokenExtensionGrant : ITokenExtensionGrant
{
    public const string ExtensionGrantName = "Impersonation";

    /// <summary>请求体参数名：管理员当前有效 access_token（用于反查身份，非本次签发对象）。</summary>
    private const string CallerAccessTokenParameterName = "access_token";

    /// <summary>请求体参数名：目标账号 IdentityUser.Id（Guid 字符串）。</summary>
    private const string TargetUserIdParameterName = "target_user_id";

    public string Name => ExtensionGrantName;

    public async Task<IActionResult> HandleAsync(ExtensionGrantContext context)
    {
        var services = context.HttpContext.RequestServices;
        var logger = services.GetRequiredService<ILogger<ImpersonationTokenExtensionGrant>>();

        var callerAccessToken = context.Request.GetParameter(CallerAccessTokenParameterName)?.ToString();
        var targetUserIdRaw = context.Request.GetParameter(TargetUserIdParameterName)?.ToString();

        if (string.IsNullOrWhiteSpace(callerAccessToken)
            || string.IsNullOrWhiteSpace(targetUserIdRaw)
            || !Guid.TryParse(targetUserIdRaw, out var targetUserId))
        {
            return Forbid(Errors.InvalidRequest, "缺少必要参数。");
        }

        // 1. 反查 caller 身份：管理员传来的是"我当前的 access_token"（不是本次要签的目标 token），
        //    经 OpenIddict introspection 管线校验其有效性并取出 principal（同 ABP 官方样板
        //    modules/openiddict/app/OpenIddict.Demo.Server/ExtensionGrants/MyTokenExtensionGrant.cs
        //    的 HandleUserAccessTokenAsync；lsp-csharp MCP 未完成一次性配置、definition 查询空转，
        //    改用反射探针核对本地 ABP 10.1.0 包实际签名，与该样板一致）。
        var callerPrincipal = await IntrospectAsync(context, callerAccessToken, services);
        if (callerPrincipal == null)
        {
            return Forbid(Errors.InvalidGrant, "身份验证失败，请重新登录后重试。");
        }

        // 2. 四条防护规则（含权限检查）全部收在 ImpersonationGuardService，规则细节见该类。
        var guard = services.GetRequiredService<ImpersonationGuardService>();
        var result = await guard.ValidateAsync(callerPrincipal, targetUserId);
        if (!result.IsAllowed)
        {
            var (error, description) = MapDenyReason(result.DenyReason);
            logger.LogWarning(
                "模拟登录被拒：reason={Reason} callerId={CallerId} targetUserId={TargetUserId} ip={Ip}",
                result.DenyReason, callerPrincipal.FindUserId(), targetUserId, context.HttpContext.Connection.RemoteIpAddress);
            return Forbid(error, description);
        }

        var callerUser = result.Caller!;
        var targetUser = result.Target!;

        // 3. 用目标用户造新 principal——与正常登录同一条 claims 工厂管线，claims/角色/租户齐全。
        var claimsPrincipalFactory = services.GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>();
        var targetPrincipal = await claimsPrincipalFactory.CreateAsync(targetUser);

        // 4. 打上 ABP 标准 impersonator claim——AbpAuditLogs.ImpersonatorUserId/UserName 列自动落值，
        //    ICurrentUser.FindImpersonatorUserId() 也据此判断"当前是否处于被模拟状态"
        //    （下次再模拟即被规则①拒绝，实现禁链式）。
        targetPrincipal.SetClaim(AbpClaimTypes.ImpersonatorUserId, callerUser.Id.ToString());
        targetPrincipal.SetClaim(AbpClaimTypes.ImpersonatorUserName, callerUser.UserName);

        // 5. scopes：请求 scope 剔除 offline_access——服务端强制剔除、不信任客户端传参（安全红线②）。
        var scopes = context.Request.GetScopes()
            .Where(s => s != Scopes.OfflineAccess)
            .ToImmutableArray();
        targetPrincipal.SetScopes(scopes);
        targetPrincipal.SetResources(await GetResourcesAsync(services, scopes));

        await services.GetRequiredService<AbpOpenIddictClaimsPrincipalManager>().HandleAsync(context.Request, targetPrincipal);

        logger.LogInformation(
            "模拟登录成功：callerId={CallerId} callerUserName={CallerUserName} -> targetUserId={TargetUserId} targetUserName={TargetUserName} ip={Ip}",
            callerUser.Id, callerUser.UserName, targetUser.Id, targetUser.UserName, context.HttpContext.Connection.RemoteIpAddress);

        return new SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, targetPrincipal);
    }

    /// <summary>经 OpenIddict introspection 管线校验一个不透明 access_token 字符串、取出其 principal。</summary>
    private static async Task<ClaimsPrincipal?> IntrospectAsync(ExtensionGrantContext context, string token, IServiceProvider services)
    {
        var transaction = await services.GetRequiredService<IOpenIddictServerFactory>().CreateTransactionAsync();
        transaction.EndpointType = OpenIddictServerEndpointType.Introspection;
        transaction.Request = new OpenIddictRequest
        {
            ClientId = context.Request.ClientId,
            ClientSecret = context.Request.ClientSecret,
            Token = token
        };

        var notification = new OpenIddictServerEvents.ProcessAuthenticationContext(transaction);
        var dispatcher = services.GetRequiredService<IOpenIddictServerDispatcher>();
        await dispatcher.DispatchAsync(notification);

        return notification.IsRejected ? null : notification.GenericTokenPrincipal;
    }

    private static async Task<List<string>> GetResourcesAsync(IServiceProvider services, ImmutableArray<string> scopes)
    {
        var resources = new List<string>();
        if (scopes.IsDefaultOrEmpty)
        {
            return resources;
        }

        await foreach (var resource in services.GetRequiredService<IOpenIddictScopeManager>().ListResourcesAsync(scopes))
        {
            resources.Add(resource);
        }

        return resources;
    }

    /// <summary>拒绝原因 → 标准 OAuth error + 模糊文案。不按目标相关原因(不存在/停用/admin不对称)细分文案，防探测。</summary>
    private static (string Error, string Description) MapDenyReason(ImpersonationDenyReason reason) => reason switch
    {
        ImpersonationDenyReason.AlreadyImpersonating => (Errors.InvalidGrant, "当前处于模拟登录状态，请先切回后再操作。"),
        ImpersonationDenyReason.PermissionDenied => (Errors.InvalidGrant, "无权执行模拟登录。"),
        ImpersonationDenyReason.SelfImpersonation => (Errors.InvalidGrant, "不可模拟本人账号。"),
        ImpersonationDenyReason.TargetUnavailable => (Errors.InvalidGrant, "目标账号不可用。"),
        _ => (Errors.InvalidGrant, "无法完成模拟登录请求。"),
    };

    private static ForbidResult Forbid(string error, string description)
        => new(
            new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }));
}
