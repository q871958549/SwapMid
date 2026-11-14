using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace SwapMid.Infrastructure;

internal static class SqlServerConnectionStringHelper
{
    private const string JdbcSqlServerPrefix = "jdbc:sqlserver://";

    public static string? GetConfiguredConnectionString(
        IConfiguration configuration,
        string connectionName,
        params string[] aliases)
    {
        var connectionString = configuration.GetConnectionString(connectionName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return WithDefaultCommandTimeout(Normalize(connectionString), configuration, connectionName);
        }

        foreach (var alias in BuildAliasList(connectionName, aliases))
        {
            foreach (var sectionPath in BuildSectionPaths(alias))
            {
                var section = configuration.GetSection(sectionPath);
                var url = FirstText(section["url"], section["Url"], section["connectionString"], section["ConnectionString"]);
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var resolved = Normalize(ApplyCredentials(
                    url,
                    FirstText(section["username"], section["userName"], section["UserName"], section["user id"], section["User ID"]),
                    FirstText(section["password"], section["Password"])));
                return WithDefaultCommandTimeout(resolved, configuration, connectionName);
            }
        }

        return null;
    }

    /// <summary>ADR-0010 §4.2：连接串级默认命令超时注入，全部经本 helper 取串的调用零手改受控。
    /// 取值 <c>ExternalDataSources:{name}:CommandTimeoutSeconds</c> → 回退
    /// <c>ExternalDataSources:Defaults:CommandTimeoutSeconds</c> → 回退 30（= provider 隐式默认，行为零变化）。</summary>
    private static string WithDefaultCommandTimeout(string connectionString, IConfiguration configuration, string connectionName)
    {
        var seconds = configuration.GetValue<int?>($"ExternalDataSources:{connectionName}:CommandTimeoutSeconds")
            ?? configuration.GetValue<int?>("ExternalDataSources:Defaults:CommandTimeoutSeconds")
            ?? 30;
        return ConnectionStringTimeoutInjector.AppendSqlServerOrPostgresCommandTimeout(connectionString, seconds);
    }

    public static string Normalize(string connectionString)
    {
        var trimmed = connectionString.Trim();
        if (!trimmed.StartsWith(JdbcSqlServerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var segments = trimmed[JdbcSqlServerPrefix.Length..]
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
        {
            return trimmed;
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = NormalizeDataSource(segments[0])
        };

        builder["Encrypt"] = false;
        builder["TrustServerCertificate"] = true;

        foreach (var segment in segments.Skip(1))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = NormalizeKey(segment[..separatorIndex]);
            var value = segment[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            switch (key)
            {
                case "databasename":
                case "database":
                case "initialcatalog":
                    builder.InitialCatalog = value;
                    break;
                case "user":
                case "username":
                case "userid":
                case "uid":
                    builder.UserID = value;
                    break;
                case "password":
                case "pwd":
                    builder.Password = value;
                    break;
                case "encrypt":
                    builder["Encrypt"] = value;
                    break;
                case "trustservercertificate":
                    builder["TrustServerCertificate"] = value;
                    break;
                case "connecttimeout":
                case "logintimeout":
                    builder["Connect Timeout"] = value;
                    break;
                case "applicationname":
                    builder.ApplicationName = value;
                    break;
                case "integratedsecurity":
                    builder.IntegratedSecurity = IsTrue(value);
                    break;
                case "multipleactiveresultsets":
                    builder.MultipleActiveResultSets = IsTrue(value);
                    break;
            }
        }

        return builder.ConnectionString;
    }

    private static string ApplyCredentials(string connectionString, string username, string password)
    {
        var normalized = Normalize(connectionString);
        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
        {
            return normalized;
        }

        var builder = new SqlConnectionStringBuilder(normalized);
        if (!string.IsNullOrWhiteSpace(username))
        {
            builder.UserID = username;
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    private static IEnumerable<string> BuildAliasList(string connectionName, string[] aliases)
    {
        yield return connectionName;

        foreach (var alias in aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
        {
            yield return alias;
        }
    }

    private static IEnumerable<string> BuildSectionPaths(string alias)
    {
        yield return alias;
        yield return $"ExternalDataSources:{alias}";
        yield return $"DataSources:{alias}";
    }

    private static string NormalizeDataSource(string dataSource)
    {
        var text = dataSource.Trim();
        var colonIndex = text.LastIndexOf(':');
        if (colonIndex <= 0 || text.Contains(','))
        {
            return text;
        }

        var host = text[..colonIndex];
        var port = text[(colonIndex + 1)..];
        return int.TryParse(port, out _) ? $"{host},{port}" : text;
    }

    private static string NormalizeKey(string key)
        => new(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsTrue(string value)
        => value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("1", StringComparison.OrdinalIgnoreCase);

    private static string FirstText(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
