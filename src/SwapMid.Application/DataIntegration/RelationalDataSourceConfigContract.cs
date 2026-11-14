using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SwapMid.DataIntegration;

/// <summary>关系型数据源的公开白名单、写入 schema 与凭据作用域契约。</summary>
internal static partial class RelationalDataSourceConfigContract
{
    internal static DataSourceProtectedConfig SanitizeForRead(JsonObject root)
    {
        var output = new JsonObject();
        var consumedKeys = new HashSet<string>(StringComparer.Ordinal);
        var hasSecret = false;

        CopyProvider(root, output, consumedKeys, ref hasSecret);
        CopySafeString(root, output, consumedKeys, "host", IsSafeHost, ref hasSecret);
        CopyPort(root, output, consumedKeys, ref hasSecret);
        CopySafeString(root, output, consumedKeys, "database", IsSafeIdentityText, ref hasSecret);
        CopySafeString(root, output, consumedKeys, "username", IsSafeIdentityText, ref hasSecret, "userName");
        CopySafeString(
            root,
            output,
            consumedKeys,
            "targetResourceGroup",
            IsValidTargetResourceGroup,
            ref hasSecret);
        CopyBoolean(root, output, consumedKeys, "encrypt", ref hasSecret);
        CopyBoolean(root, output, consumedKeys, "trustServerCertificate", ref hasSecret);

        foreach (var property in root)
        {
            var normalizedName = DataSourceConfigJson.NormalizeName(property.Key);
            if (consumedKeys.Contains(property.Key))
            {
                continue;
            }

            // password、connectionString 以及任何未知非空字段都不进入响应。
            // 未知键无法证明安全，HasSecret 保守置 true。
            if (DataSourceConfigJson.HasMeaningfulContent(property.Value) ||
                normalizedName is "password" or "connectionstring")
            {
                hasSecret = true;
            }
        }

        return new DataSourceProtectedConfig(output.ToJsonString(), hasSecret);
    }

    internal static RelationalDataSourceWriteConfig ParseForWrite(JsonObject config)
    {
        var provider = NormalizeProvider(
            DataSourceConfigJson.ReadOptionalStringStrict(config, "provider"));
        if (provider is not ("sqlserver" or "mysql" or "postgresql"))
        {
            throw new DataSourceConfigFormatException();
        }

        var host = (DataSourceConfigJson.ReadOptionalStringStrict(config, "host") ?? string.Empty)
            .Trim()
            .TrimEnd('.');
        if (!IsSafeHost(host))
        {
            throw new DataSourceConfigFormatException();
        }

        var port = DataSourceConfigJson.ReadOptionalIntStrict(config, "port") ??
                   provider switch
                   {
                       "mysql" => 3306,
                       "postgresql" => 5432,
                       _ => 1433,
                   };
        if (port is < 1 or > 65535)
        {
            throw new DataSourceConfigFormatException();
        }

        var database =
            (DataSourceConfigJson.ReadOptionalStringStrict(config, "database") ?? string.Empty).Trim();
        var username =
            (DataSourceConfigJson.ReadOptionalStringStrict(config, "username", "userName") ?? string.Empty).Trim();
        if (!IsSafeIdentityText(database) || !IsSafeIdentityText(username))
        {
            throw new DataSourceConfigFormatException();
        }

        var password = DataSourceConfigJson.ReadOptionalStringStrict(config, "password");
        var targetResourceGroup =
            (DataSourceConfigJson.ReadOptionalStringStrict(
                config,
                "targetResourceGroup") ?? string.Empty).Trim();
        if (targetResourceGroup.Length > 0 &&
            !IsValidTargetResourceGroup(targetResourceGroup))
        {
            throw new DataSourceConfigFormatException();
        }
        var encrypt = DataSourceConfigJson.ReadOptionalBoolStrict(config, "encrypt") ?? false;
        var trustServerCertificate =
            DataSourceConfigJson.ReadOptionalBoolStrict(config, "trustServerCertificate") ?? true;
        return new RelationalDataSourceWriteConfig(
            provider,
            host.ToLowerInvariant(),
            port,
            database,
            username,
            encrypt,
            trustServerCertificate,
            password,
            targetResourceGroup);
    }

    private static void CopyProvider(
        JsonObject source,
        JsonObject destination,
        ISet<string> consumedKeys,
        ref bool hasSecret)
    {
        if (!DataSourceConfigJson.TryFindProperty(
                source,
                "provider",
                out var actualName,
                out var value))
        {
            return;
        }

        consumedKeys.Add(actualName);
        if (!DataSourceConfigJson.TryGetString(value, out var provider))
        {
            hasSecret |= DataSourceConfigJson.HasMeaningfulContent(value);
            return;
        }

        provider = NormalizeProvider(provider);
        if (provider is not ("sqlserver" or "mysql" or "postgresql"))
        {
            hasSecret |= provider.Length > 0;
            return;
        }

        destination["provider"] = provider;
    }

    private static void CopySafeString(
        JsonObject source,
        JsonObject destination,
        ISet<string> consumedKeys,
        string outputName,
        Func<string, bool> validator,
        ref bool hasSecret,
        params string[] aliases)
    {
        foreach (var name in new[] { outputName }.Concat(aliases))
        {
            if (!DataSourceConfigJson.TryFindProperty(
                    source,
                    name,
                    out var actualName,
                    out var value))
            {
                continue;
            }

            consumedKeys.Add(actualName);
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

            destination[outputName] = outputName == "host"
                ? text.TrimEnd('.').ToLowerInvariant()
                : text;
            return;
        }
    }

    private static void CopyPort(
        JsonObject source,
        JsonObject destination,
        ISet<string> consumedKeys,
        ref bool hasSecret)
    {
        if (!DataSourceConfigJson.TryFindProperty(
                source,
                "port",
                out var actualName,
                out var value))
        {
            return;
        }

        consumedKeys.Add(actualName);
        var port = DataSourceConfigJson.ReadIntValue(value);
        if (port is < 1 or > 65535)
        {
            hasSecret |= DataSourceConfigJson.HasMeaningfulContent(value);
            return;
        }

        destination["port"] = port;
    }

    private static void CopyBoolean(
        JsonObject source,
        JsonObject destination,
        ISet<string> consumedKeys,
        string name,
        ref bool hasSecret)
    {
        if (!DataSourceConfigJson.TryFindProperty(
                source,
                name,
                out var actualName,
                out var value))
        {
            return;
        }

        consumedKeys.Add(actualName);
        if (DataSourceConfigJson.TryGetBoolean(value, out var boolean))
        {
            destination[name] = boolean;
            return;
        }

        hasSecret |= DataSourceConfigJson.HasMeaningfulContent(value);
    }

    private static string NormalizeProvider(string? provider)
    {
        return provider?.Trim().ToLowerInvariant() switch
        {
            null or "" or "mssql" or "sqlserver" => "sqlserver",
            "mariadb" or "mysql" => "mysql",
            "postgresql" or "postgres" or "npgsql" => "postgresql",
            var value => value,
        };
    }

    private static bool IsSafeHost(string value)
        => !string.IsNullOrWhiteSpace(value) && SafeHostRegex().IsMatch(value);

    private static bool IsSafeIdentityText(string value)
        => !value.Any(char.IsControl) &&
           !value.Contains(';', StringComparison.Ordinal) &&
           !value.Contains('\r', StringComparison.Ordinal) &&
           !value.Contains('\n', StringComparison.Ordinal);

    internal static bool IsValidTargetResourceGroup(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           TargetResourceGroupRegex().IsMatch(value);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeHostRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex TargetResourceGroupRegex();
}

internal readonly record struct RelationalDataSourceCredentialScope(
    string Provider,
    string Host,
    int Port,
    string Database,
    string Username,
    bool Encrypt,
    bool TrustServerCertificate);

internal readonly record struct RelationalDataSourceWriteConfig(
    string Provider,
    string Host,
    int Port,
    string Database,
    string Username,
    bool Encrypt,
    bool TrustServerCertificate,
    string? Password,
    string TargetResourceGroup)
{
    internal RelationalDataSourceCredentialScope Scope => new(
        Provider,
        Host,
        Port,
        Database,
        Username,
        Encrypt,
        TrustServerCertificate);

    internal string ToJson(bool includePassword)
    {
        var json = new JsonObject
        {
            ["provider"] = Provider,
            ["host"] = Host,
            ["port"] = Port,
            ["database"] = Database,
            ["username"] = Username,
            ["encrypt"] = Encrypt,
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
