using System;
using System.Security.Claims;
using System.Threading.Tasks;
using SwapMid.Permissions;
using SwapMid.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.Authorization.Permissions;

namespace SwapMid.ApiGateway;

/// <summary>
/// API 网关双主体鉴权策略：允许持有 <c>SwapMid.ApiManagement.ApiRoutes</c> 权限的平台操作员（如测试台）
/// 或持有 OpenIddict <c>external-integration</c> scope 的 <c>client_credentials</c> 第三方客户端访问。
/// </summary>
public static class GatewayAuthorizationPolicies
{
    public const string GatewayAccessPolicy = "SwapMid.ApiGateway.Access";

    public static void AddGatewayServices(IServiceCollection services)
    {
        services.TryAddSingleton<IGatewayRateLimiter, GatewaySlidingWindowRateLimiter>();
        services.AddSingleton<IAuthorizationHandler, GatewayAccessAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(GatewayAccessPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(GatewayAccessRequirement.Instance);
            });
        });
    }

    public static string? ExtractExternalClientId(ClaimsPrincipal? principal)
    {
        if (!principal.HasExternalIntegrationScope())
        {
            return null;
        }

        var clientId = principal?.FindFirst("client_id")?.Value
            ?? principal?.FindFirst("azp")?.Value;
        return string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
    }
}

public sealed class GatewayAccessRequirement : IAuthorizationRequirement
{
    public static readonly GatewayAccessRequirement Instance = new();

    private GatewayAccessRequirement()
    {
    }
}

internal sealed class GatewayAccessAuthorizationHandler(IServiceProvider serviceProvider)
    : AuthorizationHandler<GatewayAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        GatewayAccessRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (GatewayAuthorizationPolicies.ExtractExternalClientId(context.User) != null)
        {
            context.Succeed(requirement);
            return;
        }

        var permissionChecker = serviceProvider.GetService<IPermissionChecker>();
        if (permissionChecker != null)
        {
            if (await permissionChecker.IsGrantedAsync(
                    context.User,
                    SwapMidPermissions.ApiManagement.ApiRoutes.Default))
            {
                context.Succeed(requirement);
            }

            return;
        }

        if (context.User.HasClaim(
                "permission",
                SwapMidPermissions.ApiManagement.ApiRoutes.Default))
        {
            context.Succeed(requirement);
        }
    }
}
