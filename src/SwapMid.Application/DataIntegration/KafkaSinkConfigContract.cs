using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Confluent.Kafka;

namespace SwapMid.DataIntegration;

/// <summary>Kafka 目标数据源配置契约与脱敏/凭据边界。</summary>
internal static partial class KafkaSinkConfigContract
{
    private const int DefaultKafkaPort = 9092;

    internal static DataSourceProtectedConfig SanitizeForRead(JsonObject root)
    {
        var sanitized = new JsonObject();
        var hasSecret = false;

        sanitized["provider"] = "kafka";

        if (DataSourceConfigJson.TryFindProperty(root, "bootstrapServers", out _, out var serversNode))
        {
            if (DataSourceConfigJson.TryGetString(serversNode, out var rawServers) &&
                TryNormalizeBrokers(rawServers, out var normalizedServers, out _))
            {
                sanitized["bootstrapServers"] = normalizedServers;
            }
            else
            {
                hasSecret |= DataSourceConfigJson.HasMeaningfulContent(serversNode);
            }
        }

        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, SinkConfigContractHelpers.IsSafeIdentityText, ref hasSecret, "clientId");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, IsSupportedSecurityProtocol, ref hasSecret, "securityProtocol");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, IsSupportedSaslMechanism, ref hasSecret, "saslMechanism");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, SinkConfigContractHelpers.IsSafeIdentityText, ref hasSecret, "saslUsername");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, IsSupportedAcks, ref hasSecret, "acks");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, IsSupportedCompression, ref hasSecret, "compressionType");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, RelationalDataSourceConfigContract.IsValidTargetResourceGroup, ref hasSecret, "targetResourceGroup");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, IsSafeSchemaRegistryUrl, ref hasSecret, "schemaRegistryUrl");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, SinkConfigContractHelpers.IsSafeIdentityText, ref hasSecret, "schemaRegistryUsername");
        SinkConfigContractHelpers.MarkSecretPresence(root, ref hasSecret, "saslPassword");
        SinkConfigContractHelpers.MarkSecretPresence(root, ref hasSecret, "schemaRegistryPassword");

        return new DataSourceProtectedConfig(sanitized.ToJsonString(), hasSecret);
    }

    internal static string NormalizeForWrite(JsonObject root)
        => ParseForWrite(root, requirePasswordIfSasl: true).ToJson(includePassword: true);

    internal static string MergeForWrite(JsonObject existingRoot, JsonObject submittedRoot)
    {
        var existing = ParseForWrite(existingRoot, requirePasswordIfSasl: false);
        var submitted = ParseForWrite(submittedRoot, requirePasswordIfSasl: false);

        if (existing.Scope != submitted.Scope &&
            submitted.RequiresSasl &&
            string.IsNullOrWhiteSpace(submitted.SaslPassword))
        {
            throw SinkConfigContractHelpers.CredentialScopeChanged();
        }

        var effectivePassword = !string.IsNullOrWhiteSpace(submitted.SaslPassword)
            ? submitted.SaslPassword
            : (submitted.RequiresSasl ? existing.SaslPassword : null);

        if (submitted.RequiresSasl && string.IsNullOrWhiteSpace(effectivePassword))
        {
            throw SinkConfigContractHelpers.InvalidConfig("启用 SASL 认证时必须提供 saslPassword。");
        }

        var sameRegistryScope = string.Equals(existing.SchemaRegistryUrl, submitted.SchemaRegistryUrl, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(existing.SchemaRegistryUsername, submitted.SchemaRegistryUsername, StringComparison.Ordinal);
        var effectiveRegistryPassword = !string.IsNullOrWhiteSpace(submitted.SchemaRegistryPassword)
            ? submitted.SchemaRegistryPassword
            : (sameRegistryScope && !string.IsNullOrWhiteSpace(submitted.SchemaRegistryUrl) ? existing.SchemaRegistryPassword : null);

        return (submitted with { SaslPassword = effectivePassword, SchemaRegistryPassword = effectiveRegistryPassword }).ToJson(includePassword: true);
    }

    internal static KafkaSinkConfig ParseForWrite(string? configJson, bool requirePasswordIfSasl = false)
    {
        if (!DataSourceConfigJson.TryParseObject(configJson, out var root))
        {
            throw SinkConfigContractHelpers.InvalidConfig("Kafka 配置必须是合法的 JSON 对象。");
        }

        return ParseForWrite(root, requirePasswordIfSasl);
    }

    internal static KafkaSinkConfig ParseForWrite(JsonObject root, bool requirePasswordIfSasl)
    {
        var rawServers = DataSourceConfigJson.ReadOptionalStringStrict(root, "bootstrapServers")?.Trim() ?? string.Empty;
        if (!TryNormalizeBrokers(rawServers, out var bootstrapServers, out var brokers))
        {
            throw SinkConfigContractHelpers.InvalidConfig("Kafka bootstrapServers 必须是 1~8 个有效的 host:port 节点。");
        }

        var clientId = SinkConfigContractHelpers.ReadOptionalValidatedString(
            root, "swapmid-etl", SinkConfigContractHelpers.IsSafeIdentityText, "Kafka clientId 包含非法字符。", "clientId");

        var securityProtocol = NormalizeSecurityProtocol(
            DataSourceConfigJson.ReadOptionalStringStrict(root, "securityProtocol"));
        var requiresSasl = securityProtocol is SecurityProtocol.SaslPlaintext or SecurityProtocol.SaslSsl;

        string? saslMechanism = null;
        string? saslUsername = null;
        string? saslPassword = null;
        if (requiresSasl)
        {
            saslMechanism = NormalizeSaslMechanism(
                DataSourceConfigJson.ReadOptionalStringStrict(root, "saslMechanism"));
            saslUsername = SinkConfigContractHelpers.ReadRequiredString(
                root,
                "启用 SASL 认证时必须提供 saslUsername。",
                SinkConfigContractHelpers.IsSafeIdentityText,
                "saslUsername 包含非法字符。",
                "saslUsername");
            saslPassword = DataSourceConfigJson.ReadOptionalStringStrict(root, "saslPassword");
            if (requirePasswordIfSasl && string.IsNullOrWhiteSpace(saslPassword))
            {
                throw SinkConfigContractHelpers.InvalidConfig("启用 SASL 认证时必须提供 saslPassword。");
            }
        }

        var acks = NormalizeAcks(DataSourceConfigJson.ReadOptionalStringStrict(root, "acks"));
        var compression = NormalizeCompression(DataSourceConfigJson.ReadOptionalStringStrict(root, "compressionType"));
        var targetResourceGroup = SinkConfigContractHelpers.ReadOptionalTargetResourceGroup(root);
        var schemaRegistryUrl = NormalizeOptionalSchemaRegistryUrl(
            DataSourceConfigJson.ReadOptionalStringStrict(root, "schemaRegistryUrl"));
        string? schemaRegistryUsername = null;
        string? schemaRegistryPassword = null;
        if (!string.IsNullOrWhiteSpace(schemaRegistryUrl))
        {
            var rawSrUser = DataSourceConfigJson.ReadOptionalStringStrict(root, "schemaRegistryUsername")?.Trim();
            if (!string.IsNullOrWhiteSpace(rawSrUser))
            {
                if (!SinkConfigContractHelpers.IsSafeIdentityText(rawSrUser))
                {
                    throw SinkConfigContractHelpers.InvalidConfig("Kafka schemaRegistryUsername 包含非法字符。");
                }

                schemaRegistryUsername = rawSrUser;
                schemaRegistryPassword = DataSourceConfigJson.ReadOptionalStringStrict(root, "schemaRegistryPassword");
            }
        }

        return new KafkaSinkConfig(
            bootstrapServers,
            brokers,
            clientId,
            securityProtocol,
            saslMechanism,
            saslUsername,
            saslPassword,
            acks,
            compression,
            targetResourceGroup,
            schemaRegistryUrl,
            schemaRegistryUsername,
            schemaRegistryPassword);
    }

    internal static bool IsValidTopicName(string? topic)
        => !string.IsNullOrWhiteSpace(topic) &&
           topic != "." &&
           topic != ".." &&
           TopicNameRegex().IsMatch(topic);

    internal static bool TryNormalizeBrokers(
        string rawServers,
        out string normalized,
        out IReadOnlyList<(string Host, int Port)> brokers)
    {
        normalized = string.Empty;
        brokers = Array.Empty<(string, int)>();
        if (string.IsNullOrWhiteSpace(rawServers))
        {
            return false;
        }

        var parsed = new List<(string Host, int Port)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in rawServers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (parsed.Count >= 8)
            {
                return false;
            }

            if (!TryParseBrokerEndpoint(entry, out var host, out var port))
            {
                return false;
            }

            var canonical = $"{host}:{port.ToString(CultureInfo.InvariantCulture)}";
            if (seen.Add(canonical))
            {
                parsed.Add((host, port));
            }
        }

        if (parsed.Count == 0)
        {
            return false;
        }

        brokers = parsed;
        normalized = string.Join(",", parsed.Select(b => $"{b.Host}:{b.Port.ToString(CultureInfo.InvariantCulture)}"));
        return true;
    }

    private static bool TryParseBrokerEndpoint(string entry, out string host, out int port)
    {
        host = string.Empty;
        port = DefaultKafkaPort;

        if (entry.StartsWith('['))
        {
            var closeBracket = entry.IndexOf(']');
            if (closeBracket <= 1)
            {
                return false;
            }

            host = entry[1..closeBracket].Trim().ToLowerInvariant();
            if (!SinkConfigContractHelpers.IsSafeHost(host))
            {
                return false;
            }

            var remainder = entry[(closeBracket + 1)..];
            if (string.IsNullOrEmpty(remainder))
            {
                return true;
            }

            if (!remainder.StartsWith(':') ||
                !int.TryParse(remainder[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port) ||
                port is < 1 or > 65535)
            {
                return false;
            }

            return true;
        }

        var firstColon = entry.IndexOf(':');
        var lastColon = entry.LastIndexOf(':');
        if (firstColon != lastColon)
        {
            return false;
        }

        if (firstColon < 0)
        {
            host = entry.Trim().TrimEnd('.').ToLowerInvariant();
            return SinkConfigContractHelpers.IsSafeHost(host);
        }

        host = entry[..firstColon].Trim().TrimEnd('.').ToLowerInvariant();
        var portPart = entry[(firstColon + 1)..].Trim();
        return SinkConfigContractHelpers.IsSafeHost(host) &&
               int.TryParse(portPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) &&
               port is >= 1 and <= 65535;
    }

    private static SecurityProtocol NormalizeSecurityProtocol(string? raw)
        => raw?.Trim().ToLowerInvariant() switch
        {
            null or "" or "plaintext" => SecurityProtocol.Plaintext,
            "ssl" => SecurityProtocol.Ssl,
            "saslplaintext" or "sasl_plaintext" => SecurityProtocol.SaslPlaintext,
            "saslssl" or "sasl_ssl" => SecurityProtocol.SaslSsl,
            _ => throw SinkConfigContractHelpers.InvalidConfig("Kafka securityProtocol 仅支持 Plaintext、Ssl、SaslPlaintext 或 SaslSsl。"),
        };

    private static string NormalizeSaslMechanism(string? raw)
        => raw?.Trim().ToLowerInvariant() switch
        {
            null or "" or "plain" => "Plain",
            "scram-sha-256" or "scramsha256" => "ScramSha256",
            "scram-sha-512" or "scramsha512" => "ScramSha512",
            _ => throw SinkConfigContractHelpers.InvalidConfig("Kafka saslMechanism 仅支持 Plain、ScramSha256 或 ScramSha512。"),
        };

    private static string NormalizeAcks(string? raw)
        => raw?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" or "-1" => "All",
            "leader" or "1" => "Leader",
            _ => throw SinkConfigContractHelpers.InvalidConfig("Kafka acks 仅支持 All 或 Leader。"),
        };

    private static string NormalizeCompression(string? raw)
        => raw?.Trim().ToLowerInvariant() switch
        {
            null or "" or "snappy" => "Snappy",
            "none" => "None",
            "gzip" => "Gzip",
            "zstd" => "Zstd",
            _ => throw SinkConfigContractHelpers.InvalidConfig("Kafka compressionType 仅支持 None、Gzip、Snappy 或 Zstd。"),
        };

    private static bool IsSupportedSecurityProtocol(string value)
        => value.Trim().ToLowerInvariant() is "plaintext" or "ssl" or "saslplaintext" or "sasl_plaintext" or "saslssl" or "sasl_ssl";

    private static bool IsSupportedSaslMechanism(string value)
        => value.Trim().ToLowerInvariant() is "plain" or "scram-sha-256" or "scramsha256" or "scram-sha-512" or "scramsha512";

    private static bool IsSupportedAcks(string value)
        => value.Trim().ToLowerInvariant() is "all" or "-1" or "leader" or "1";

    private static bool IsSupportedCompression(string value)
        => value.Trim().ToLowerInvariant() is "none" or "gzip" or "snappy" or "zstd";

    private static bool IsSafeSchemaRegistryUrl(string value)
        => TryNormalizeSchemaRegistryUrl(value, out _);

    private static string? NormalizeOptionalSchemaRegistryUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return TryNormalizeSchemaRegistryUrl(raw, out var normalized)
            ? normalized
            : throw SinkConfigContractHelpers.InvalidConfig("Kafka schemaRegistryUrl 必须是合法的 http/https 地址（不得包含内嵌凭据或查询参数）。");
    }

    private static bool TryNormalizeSchemaRegistryUrl(string raw, out string normalized)
    {
        normalized = string.Empty;
        var trimmed = raw.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !SinkConfigContractHelpers.IsSafeHost(uri.Host))
        {
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return true;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex TopicNameRegex();
}
