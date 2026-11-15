using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>ClickHouse / ObjectStorage / Kafka 目标配置契约共用验证与读取辅助。</summary>
internal static partial class SinkConfigContractHelpers
{
    internal static bool IsExternalAnalyticalSink(this DataSourceType type)
        => type is DataSourceType.Kafka or DataSourceType.ClickHouse or DataSourceType.ObjectStorage;

    internal static bool IsEtlSupportedTarget(this DataSourceType type)
        => type == DataSourceType.RelationalDatabase || type.IsExternalAnalyticalSink();

    internal static bool SupportsTypedConfig(this DataSourceType type)
        => type == DataSourceType.RelationalDatabase || type.IsExternalAnalyticalSink();

    internal static string ReadRequiredString(
        JsonObject obj,
        string reasonIfMissing,
        Func<string, bool> validator,
        string reasonIfInvalid,
        params string[] names)
    {
        var raw = DataSourceConfigJson.ReadOptionalStringStrict(obj, names)?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw InvalidConfig(reasonIfMissing);
        }

        if (!validator(raw))
        {
            throw InvalidConfig(reasonIfInvalid);
        }

        return raw;
    }

    internal static string ReadOptionalValidatedString(
        JsonObject obj,
        string defaultValue,
        Func<string, bool> validator,
        string reasonIfInvalid,
        params string[] names)
    {
        var raw = DataSourceConfigJson.ReadOptionalStringStrict(obj, names)?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return defaultValue;
        }

        if (!validator(raw))
        {
            throw InvalidConfig(reasonIfInvalid);
        }

        return raw;
    }

    internal static string ReadOptionalTargetResourceGroup(JsonObject obj)
    {
        var group = DataSourceConfigJson.ReadOptionalStringStrict(obj, "targetResourceGroup")?.Trim() ?? string.Empty;
        if (group.Length > 0 && !RelationalDataSourceConfigContract.IsValidTargetResourceGroup(group))
        {
            throw InvalidConfig("targetResourceGroup 仅允许字母、数字、点、下划线、冒号与连字符。");
        }

        return group;
    }

    internal static void CopySanitizedString(
        JsonObject source,
        JsonObject destination,
        Func<string, bool> validator,
        ref bool hasSecret,
        string outputName,
        bool lowercaseHost = false,
        params string[] aliases)
    {
        foreach (var name in new[] { outputName }.Concat(aliases))
        {
            if (!DataSourceConfigJson.TryFindProperty(source, name, out _, out var value))
            {
                continue;
            }

            if (!DataSourceConfigJson.TryGetString(value, out var text))
            {
                hasSecret |= DataSourceConfigJson.HasMeaningfulContent(value);
                return;
            }

            text = text.Trim();
            if (!validator(text))
            {
                hasSecret |= text.Length > 0;
                return;
            }

            destination[outputName] = lowercaseHost
                ? text.TrimEnd('.').ToLowerInvariant()
                : text;
            return;
        }
    }

    internal static void MarkSecretPresence(JsonObject source, ref bool hasSecret, params string[] names)
    {
        foreach (var name in names)
        {
            if (DataSourceConfigJson.TryFindProperty(source, name, out _, out var value))
            {
                hasSecret |= DataSourceConfigJson.HasMeaningfulContent(value);
            }
        }
    }

    internal static bool IsSafeHost(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 255 && SafeHostRegex().IsMatch(value);

    internal static bool IsSafeIdentifier(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && SafeIdentifierRegex().IsMatch(value);

    internal static bool IsSafeIdentityText(string value)
        => value.Length <= 256 &&
           !value.Any(char.IsControl) &&
           !value.Contains(';', StringComparison.Ordinal) &&
           !value.Contains('\r', StringComparison.Ordinal) &&
           !value.Contains('\n', StringComparison.Ordinal);

    internal static Exception InvalidConfig(string reason)
        => new DataSourceConfigFormatException();

    internal static Exception CredentialScopeChanged()
        => new DataSourceCredentialScopeChangedException();

    internal static string RedactDiagnosticText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "未知连接或响应错误。";
        }

        var normalized = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
        normalized = UserInfoRegex().Replace(normalized, "$1***@");
        normalized = SecretQueryParamRegex().Replace(normalized, "$1=***");
        return normalized.Length <= 240 ? normalized : normalized[..240];
    }

    [GeneratedRegex(@"(://)[^/\s@:]+:[^/\s@]+@", RegexOptions.CultureInvariant)]
    private static partial Regex UserInfoRegex();

    [GeneratedRegex(@"(?i)(password|pwd|secret|secretKey|token|key|sig|signature)=([^\s&;,]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SecretQueryParamRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeHostRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifierRegex();
}
