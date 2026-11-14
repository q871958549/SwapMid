using System;
using System.Linq;
using System.Security.Claims;

namespace SwapMid.Security;

/// <summary>
/// OpenIddict Token Scope 判定工具：统一处理单值/多值 <c>scope</c> claim（空格分隔字符串与多条同名 claim 兼容）
/// 与 OpenIddict 原生 <c>oi_scp</c> claim。
/// </summary>
public static class OpenIddictScopeChecker
{
    public const string PlatformScope = "SwapMid";
    public const string ExternalIntegrationScope = "external-integration";

    public static bool HasPlatformScope(this ClaimsPrincipal? principal)
        => HasScope(principal, PlatformScope);

    public static bool HasExternalIntegrationScope(this ClaimsPrincipal? principal)
        => HasScope(principal, ExternalIntegrationScope);

    public static bool HasScope(this ClaimsPrincipal? principal, string scope)
    {
        if (principal is null || string.IsNullOrWhiteSpace(scope))
        {
            return false;
        }

        foreach (var claim in principal.FindAll("scope"))
        {
            if (claim.Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(scope, StringComparer.Ordinal))
            {
                return true;
            }
        }

        foreach (var claim in principal.FindAll("oi_scp"))
        {
            if (string.Equals(claim.Value, scope, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
