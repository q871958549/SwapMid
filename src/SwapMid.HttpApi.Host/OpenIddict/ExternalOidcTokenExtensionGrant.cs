using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using static OpenIddict.Abstractions.OpenIddictConstants;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using SignInResult = Microsoft.AspNetCore.Mvc.SignInResult;

namespace SwapMid.OpenIddict;

/// <summary>
/// 外部 OIDC 身份联邦 Extension Grant（<c>grant_type=external_oidc</c>）：
/// 由 Next.js BFF 在完成 OIDC 授权码 + PKCE 回调后以服务端对服务端方式调用，
/// 在 .NET 侧完成外部 IdP 令牌交换与 JWKS 验签，零浏览器令牌暴露。
/// </summary>
public sealed class ExternalOidcTokenExtensionGrant : ITokenExtensionGrant
{
    public const string ExtensionGrantName = "external_oidc";
    public const string LoginProviderName = "ExternalOidc";

    public string Name => ExtensionGrantName;

    public async Task<IActionResult> HandleAsync(ExtensionGrantContext context)
    {
        var services = context.HttpContext.RequestServices;
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILogger<ExternalOidcTokenExtensionGrant>>();

        if (!bool.TryParse(configuration["AuthServer:ExternalOidc:Enabled"], out var enabled) || !enabled)
        {
            return Forbid(Errors.InvalidGrant, "外部 OIDC 单点登录未启用。");
        }

        var code = context.Request.GetParameter("code")?.ToString();
        var redirectUri = context.Request.GetParameter("redirect_uri")?.ToString();
        var codeVerifier = context.Request.GetParameter("code_verifier")?.ToString();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(redirectUri))
        {
            return Forbid(Errors.InvalidRequest, "缺少外部 OIDC 授权码或回调地址。");
        }

        var exchanger = services.GetRequiredService<IExternalOidcCodeExchanger>();
        ExternalOidcVerifiedIdentity? verified;
        try
        {
            verified = await exchanger.ExchangeCodeAsync(
                code,
                redirectUri,
                codeVerifier,
                context.HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "外部 OIDC 授权码交换异常 exceptionType={ExceptionType}",
                ex.GetType().Name);
            return Forbid(Errors.InvalidGrant, "外部身份提供商验证失败。");
        }

        if (verified is null)
        {
            return Forbid(Errors.InvalidGrant, "外部身份令牌无效或已过期。");
        }

        var userManager = services.GetRequiredService<IdentityUserManager>();
        var user = await ResolvePlatformUserAsync(userManager, verified);
        if (user is null || !user.IsActive)
        {
            logger.LogWarning("外部 OIDC 登录被拒：未匹配到启用状态的平台账号 sub={Subject}", verified.Subject);
            return Forbid(Errors.InvalidGrant, "当前外部账号未绑定已启用的平台用户，请联系管理员开通。");
        }

        var claimsPrincipalFactory = services.GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>();
        var principal = await claimsPrincipalFactory.CreateAsync(user);

        var scopes = context.Request.GetScopes();
        principal.SetScopes(scopes);
        principal.SetResources(await GetResourcesAsync(services, scopes));

        await services.GetRequiredService<AbpOpenIddictClaimsPrincipalManager>()
            .HandleAsync(context.Request, principal);

        logger.LogInformation("外部 OIDC 单点登录成功 userId={UserId} userName={UserName}", user.Id, user.UserName);
        return new SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, principal);
    }

    private static async Task<IdentityUser?> ResolvePlatformUserAsync(
        IdentityUserManager userManager,
        ExternalOidcVerifiedIdentity verified)
    {
        var byLogin = await userManager.FindByLoginAsync(LoginProviderName, verified.Subject);
        if (byLogin is not null)
        {
            return byLogin;
        }

        IdentityUser? matched = null;
        if (!string.IsNullOrWhiteSpace(verified.Email) && verified.EmailVerified)
        {
            matched = await userManager.FindByEmailAsync(verified.Email);
        }

        if (matched is null && !string.IsNullOrWhiteSpace(verified.PreferredUsername))
        {
            matched = await userManager.FindByNameAsync(verified.PreferredUsername);
        }

        if (matched is not null && matched.IsActive)
        {
            _ = await userManager.AddLoginAsync(
                matched,
                new UserLoginInfo(LoginProviderName, verified.Subject, LoginProviderName));
        }

        return matched;
    }

    private static async Task<List<string>> GetResourcesAsync(
        IServiceProvider services,
        ImmutableArray<string> scopes)
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

    private static ForbidResult Forbid(string error, string description)
        => new(
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }));
}
