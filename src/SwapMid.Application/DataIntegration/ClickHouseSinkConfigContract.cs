using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace SwapMid.DataIntegration;

/// <summary>ClickHouse 数据源配置的脱敏读取与凭据边界契约。</summary>
internal static class ClickHouseSinkConfigContract
{
    private const int DefaultHttpPort = 8123;
    private const int DefaultHttpsPort = 8443;

    internal static DataSourceProtectedConfig SanitizeForRead(JsonObject root)
    {
        var sanitized = new JsonObject();
        var hasSecret = false;

        sanitized["provider"] = "clickhouse";
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, SinkConfigContractHelpers.IsSafeHost, ref hasSecret, "host", lowercaseHost: true);

        if (DataSourceConfigJson.TryFindProperty(root, "port", out _, out var portNode))
        {
            var port = DataSourceConfigJson.ReadIntValue(portNode);
            if (port is >= 1 and <= 65535)
            {
                sanitized["port"] = port;
            }
            else
            {
                hasSecret |= DataSourceConfigJson.HasMeaningfulContent(portNode);
            }
        }

        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, SinkConfigContractHelpers.IsSafeIdentifier, ref hasSecret, "database");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, SinkConfigContractHelpers.IsSafeIdentityText, ref hasSecret, "username", aliases: "user");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, RelationalDataSourceConfigContract.IsValidTargetResourceGroup, ref hasSecret, "targetResourceGroup");

        CopyBoolean(root, sanitized, ref hasSecret, "useTls", "encrypt");
        CopyBoolean(root, sanitized, ref hasSecret, "trustServerCertificate");
        SinkConfigContractHelpers.MarkSecretPresence(root, ref hasSecret, "password", "pwd");

        return new DataSourceProtectedConfig(sanitized.ToJsonString(), hasSecret);
    }

    internal static string NormalizeForWrite(JsonObject root)
        => ParseForWrite(root).ToJson(includePassword: true);

    internal static string MergeForWrite(JsonObject existingRoot, JsonObject submittedRoot)
    {
        var existing = ParseForWrite(existingRoot);
        var submitted = ParseForWrite(submittedRoot);

        if (existing.Scope != submitted.Scope && string.IsNullOrWhiteSpace(submitted.Password))
        {
            throw SinkConfigContractHelpers.CredentialScopeChanged();
        }

        var effectivePassword = !string.IsNullOrWhiteSpace(submitted.Password)
            ? submitted.Password
            : existing.Password;

        return (submitted with { Password = effectivePassword }).ToJson(includePassword: true);
    }

    internal static ClickHouseSinkConfig ParseForWrite(string? configJson)
    {
        if (!DataSourceConfigJson.TryParseObject(configJson, out var root))
        {
            throw SinkConfigContractHelpers.InvalidConfig("ClickHouse 配置必须是合法的 JSON 对象。");
        }

        return ParseForWrite(root);
    }

    internal static ClickHouseSinkConfig ParseForWrite(JsonObject root)
    {
        var host = SinkConfigContractHelpers.ReadRequiredString(
            root,
            "ClickHouse 配置必须包含有效的 host。",
            SinkConfigContractHelpers.IsSafeHost,
            "ClickHouse host 包含非法字符。",
            "host").TrimEnd('.').ToLowerInvariant();

        var useTls = DataSourceConfigJson.ReadOptionalBoolStrict(root, "useTls")
            ?? DataSourceConfigJson.ReadOptionalBoolStrict(root, "encrypt")
            ?? false;
        var trustServerCertificate = DataSourceConfigJson.ReadOptionalBoolStrict(root, "trustServerCertificate") ?? false;

        var defaultPort = useTls ? DefaultHttpsPort : DefaultHttpPort;
        var port = DataSourceConfigJson.ReadOptionalIntStrict(root, "port") ?? defaultPort;
        if (port is < 1 or > 65535)
        {
            throw SinkConfigContractHelpers.InvalidConfig("ClickHouse 端口号必须在 1~65535 之间。");
        }

        var database = SinkConfigContractHelpers.ReadOptionalValidatedString(
            root,
            "default",
            SinkConfigContractHelpers.IsSafeIdentifier,
            "ClickHouse database 名称格式无效。",
            "database");

        var username = SinkConfigContractHelpers.ReadOptionalValidatedString(
            root,
            "default",
            SinkConfigContractHelpers.IsSafeIdentityText,
            "ClickHouse username 包含非法控制字符。",
            "username", "user");

        var password = DataSourceConfigJson.ReadOptionalStringStrict(root, "password", "pwd");
        var targetResourceGroup = SinkConfigContractHelpers.ReadOptionalTargetResourceGroup(root);

        return new ClickHouseSinkConfig(
            host,
            port,
            database,
            username,
            password,
            useTls,
            trustServerCertificate,
            targetResourceGroup);
    }

    private static void CopyBoolean(
        JsonObject source,
        JsonObject destination,
        ref bool hasSecret,
        string outputName,
        params string[] aliases)
    {
        foreach (var name in new[] { outputName }.Concat(aliases))
        {
            if (!DataSourceConfigJson.TryFindProperty(source, name, out _, out var value))
            {
                continue;
            }

            if (DataSourceConfigJson.TryGetBoolean(value, out var b))
            {
                destination[outputName] = b;
                return;
            }

            hasSecret |= DataSourceConfigJson.HasMeaningfulContent(value);
            return;
        }
    }
}

internal readonly record struct ClickHouseCredentialScope(
    string Host,
    int Port,
    string Database,
    string Username,
    bool UseTls,
    bool TrustServerCertificate);

internal readonly record struct ClickHouseSinkConfig(
    string Host,
    int Port,
    string Database,
    string Username,
    string? Password,
    bool UseTls,
    bool TrustServerCertificate,
    string TargetResourceGroup)
{
    internal ClickHouseCredentialScope Scope => new(
        Host,
        Port,
        Database,
        Username,
        UseTls,
        TrustServerCertificate);

    internal Uri BuildBaseUri()
    {
        var scheme = UseTls ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        var hostPart = Host.Contains(':', StringComparison.Ordinal) && !Host.StartsWith('[')
            ? $"[{Host}]"
            : Host;
        return new Uri($"{scheme}://{hostPart}:{Port.ToString(CultureInfo.InvariantCulture)}/", UriKind.Absolute);
    }

    internal string ToJson(bool includePassword)
    {
        var json = new JsonObject
        {
            ["provider"] = "clickhouse",
            ["host"] = Host,
            ["port"] = Port,
            ["database"] = Database,
            ["username"] = Username,
            ["useTls"] = UseTls,
            ["trustServerCertificate"] = TrustServerCertificate,
        };
        if (!string.IsNullOrWhiteSpace(TargetResourceGroup))
        {
            json["targetResourceGroup"] = TargetResourceGroup;
        }

        if (includePassword && !string.IsNullOrWhiteSpace(Password))
        {
            json["password"] = Password;
        }

        return json.ToJsonString();
    }
}
