using System;
using System.Linq;

namespace SwapMid.Notification;

/// <summary>
/// 把通知里的平台内相对路径拼成绝对链接。只接受 <c>App:FrontendUrl</c> + 安全相对路径，
/// 拒绝调用方传入的任意 URL（防钓鱼；Webhook markdown 的 <c>[]()</c> 只允许通道自产链接）。
/// </summary>
public static class NotificationActionLink
{
    public const string DefaultLabel = "打开处理页";

    private static readonly string[] AllowedPathPrefixes =
    [
        "/dashboard",
        "/data-integration/",
        "/message-bus/",
        "/api-management/",
        "/system/",
        "/enterprise-management/",
        "/account-management/",
        "/developer-docs/",
        "/notification",
    ];

    public static bool TryCreate(string? frontendUrl, string? actionPath, out string absoluteUrl)
    {
        absoluteUrl = string.Empty;
        if (!IsSafePath(actionPath) || !TryGetBase(frontendUrl, out var baseUri))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUri, actionPath, out var result))
        {
            return false;
        }

        if (!string.Equals(result.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            || result.Scheme != baseUri.Scheme)
        {
            return false;
        }

        absoluteUrl = result.AbsoluteUri;
        return true;
    }

    public static string? ResolveFrontendUrl(string? notificationFrontendUrl, string? defaultFrontendUrl)
    {
        if (!string.IsNullOrWhiteSpace(notificationFrontendUrl))
        {
            return notificationFrontendUrl.Trim();
        }

        return string.IsNullOrWhiteSpace(defaultFrontendUrl) ? null : defaultFrontendUrl.Trim();
    }

    public static bool IsSafePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 200)
        {
            return false;
        }

        if (path[0] != '/' || (path.Length > 1 && path[1] == '/'))
        {
            return false;
        }

        if (!AllowedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return false;
        }

        if (path.Contains("://", StringComparison.Ordinal) ||
            path.Contains("..", StringComparison.Ordinal) ||
            path.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var c in path)
        {
            if (char.IsControl(c) || c is '\\' or ' ' or '<' or '>' or '[' or ']' or '"' or '\'')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetBase(string? frontendUrl, out Uri baseUri)
    {
        baseUri = null!;
        if (string.IsNullOrWhiteSpace(frontendUrl))
        {
            return false;
        }

        var trimmed = frontendUrl.Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            baseUri = uri;
            return true;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && IsLoopback(uri))
        {
            baseUri = uri;
            return true;
        }

        return false;
    }

    private static bool IsLoopback(Uri uri)
        => uri.IsLoopback
           || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
           || uri.Host is "127.0.0.1" or "::1";
}
