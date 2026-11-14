using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace SwapMid;

/// <summary>
/// 统一判定不应接收浏览器重定向或 HTML 错误页的机器端点。
/// </summary>
internal static class MachineEndpointPathPolicy
{
    private static readonly PathString[] InteractiveBrowserEndpointPrefixes =
    [
        new("/connect/authorize"),
        new("/connect/endsession"),
    ];

    private static readonly PathString[] MachineEndpointPrefixes =
    [
        new("/api"),
        new("/gateway"),
        new("/connect"),
        new("/.well-known"),
        new("/health/live"),
        new("/health/ready"),
        new("/health-status"),
        new("/metrics"),
    ];

    internal static bool IsMachineEndpoint(PathString path)
    {
        foreach (var prefix in InteractiveBrowserEndpointPrefixes)
        {
            if (path.StartsWithSegments(prefix))
            {
                return false;
            }
        }

        foreach (var prefix in MachineEndpointPrefixes)
        {
            if (path.StartsWithSegments(prefix))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ShouldUseHtmlErrorPage(PathString path)
    {
        return !IsMachineEndpoint(path);
    }

    internal static Task HandleLoginRedirectAsync(
        RedirectContext<CookieAuthenticationOptions> context)
    {
        return HandleCookieRedirectAsync(context, StatusCodes.Status401Unauthorized);
    }

    internal static Task HandleAccessDeniedRedirectAsync(
        RedirectContext<CookieAuthenticationOptions> context)
    {
        return HandleCookieRedirectAsync(context, StatusCodes.Status403Forbidden);
    }

    private static Task HandleCookieRedirectAsync(
        RedirectContext<CookieAuthenticationOptions> context,
        int machineEndpointStatusCode)
    {
        if (IsMachineEndpoint(context.Request.Path))
        {
            context.Response.StatusCode = machineEndpointStatusCode;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}
