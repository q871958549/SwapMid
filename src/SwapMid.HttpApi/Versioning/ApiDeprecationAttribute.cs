using System;
using System.Globalization;

namespace SwapMid.HttpApi.Versioning;

/// <summary>
/// 标记仍可调用、但已进入迁移期的 HTTP API。运行时会输出 RFC 9745
/// <c>Deprecation</c> 与迁移文档 <c>Link</c>；已确定停服日期时再输出 RFC 8594
/// <c>Sunset</c>。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ApiDeprecationAttribute : Attribute
{
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private string? _sunsetAtUtc;
    private DateTimeOffset? _sunsetAt;

    public ApiDeprecationAttribute(string deprecatedAtUtc, string documentationUri)
    {
        DeprecatedAt = ParseUtc(deprecatedAtUtc, nameof(deprecatedAtUtc));
        DocumentationUri = ValidateDocumentationUri(documentationUri);
    }

    /// <summary>进入废弃期的 UTC 时间。</summary>
    public DateTimeOffset DeprecatedAt { get; }

    /// <summary>迁移说明的 HTTPS 绝对地址或站内根相对地址。</summary>
    public string DocumentationUri { get; }

    /// <summary>
    /// 可选停服时间。只有退役窗口已经审批时才设置，且必须晚于废弃时间。
    /// 格式固定为 <c>yyyy-MM-ddTHH:mm:ssZ</c>。
    /// </summary>
    public string? SunsetAtUtc
    {
        get => _sunsetAtUtc;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _sunsetAtUtc = null;
                _sunsetAt = null;
                return;
            }

            var sunsetAt = ParseUtc(value, nameof(SunsetAtUtc));
            if (sunsetAt <= DeprecatedAt)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(SunsetAtUtc),
                    value,
                    "API 停服时间必须晚于进入废弃期的时间");
            }

            _sunsetAtUtc = value;
            _sunsetAt = sunsetAt;
        }
    }

    internal string DeprecationHeaderValue => $"@{DeprecatedAt.ToUnixTimeSeconds()}";

    internal DateTimeOffset? SunsetAt => _sunsetAt;

    private static DateTimeOffset ParseUtc(string value, string parameterName)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                UtcFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new ArgumentException(
                $"时间必须使用 UTC 格式 {UtcFormat}",
                parameterName);
        }

        return parsed;
    }

    private static string ValidateDocumentationUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.IndexOfAny(['\r', '\n', '<', '>', '"']) >= 0 ||
            !Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri) ||
            (uri.IsAbsoluteUri && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            (!uri.IsAbsoluteUri && !value.StartsWith("/", StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "迁移文档地址必须是 HTTPS 绝对地址或以 / 开头的站内地址",
                nameof(value));
        }

        return value;
    }
}
