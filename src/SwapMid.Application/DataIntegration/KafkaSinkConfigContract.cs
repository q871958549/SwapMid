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
        SinkConfigContractHelpers.MarkSecretPresence(root, ref hasSecret, "saslPassword");

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

        return (submitted with { SaslPassword = effectivePassword }).ToJson(includePassword: true);
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
            targetResourceGroup);
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

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex TopicNameRegex();
}

internal readonly record struct KafkaCredentialScope(
    string BootstrapServers,
    string ClientId,
    SecurityProtocol SecurityProtocol,
    string? SaslMechanism,
    string? SaslUsername);

internal readonly record struct KafkaSinkConfig(
    string BootstrapServers,
    IReadOnlyList<(string Host, int Port)> Brokers,
    string ClientId,
    SecurityProtocol SecurityProtocol,
    string? SaslMechanism,
    string? SaslUsername,
    string? SaslPassword,
    string Acks,
    string CompressionType,
    string TargetResourceGroup)
{
    internal bool RequiresSasl => SecurityProtocol is SecurityProtocol.SaslPlaintext or SecurityProtocol.SaslSsl;

    internal KafkaCredentialScope Scope => new(
        BootstrapServers,
        ClientId,
        SecurityProtocol,
        SaslMechanism,
        SaslUsername);

    internal ProducerConfig ToProducerConfig()
    {
        var config = new ProducerConfig
        {
            BootstrapServers = BootstrapServers,
            ClientId = ClientId,
            SecurityProtocol = SecurityProtocol,
            Acks = Acks == "All" ? Confluent.Kafka.Acks.All : Confluent.Kafka.Acks.Leader,
            EnableIdempotence = Acks == "All",
            CompressionType = CompressionType switch
            {
                "Gzip" => Confluent.Kafka.CompressionType.Gzip,
                "Zstd" => Confluent.Kafka.CompressionType.Zstd,
                "None" => Confluent.Kafka.CompressionType.None,
                _ => Confluent.Kafka.CompressionType.Snappy,
            },
            MessageTimeoutMs = 30_000,
            SocketTimeoutMs = 15_000,
        };

        if (RequiresSasl)
        {
            config.SaslMechanism = SaslMechanism switch
            {
                "ScramSha256" => Confluent.Kafka.SaslMechanism.ScramSha256,
                "ScramSha512" => Confluent.Kafka.SaslMechanism.ScramSha512,
                _ => Confluent.Kafka.SaslMechanism.Plain,
            };
            config.SaslUsername = SaslUsername;
            config.SaslPassword = SaslPassword;
        }

        return config;
    }

    internal AdminClientConfig ToAdminConfig()
    {
        var config = new AdminClientConfig
        {
            BootstrapServers = BootstrapServers,
            ClientId = ClientId,
            SecurityProtocol = SecurityProtocol,
            SocketTimeoutMs = 8_000,
        };

        if (RequiresSasl)
        {
            config.SaslMechanism = SaslMechanism switch
            {
                "ScramSha256" => Confluent.Kafka.SaslMechanism.ScramSha256,
                "ScramSha512" => Confluent.Kafka.SaslMechanism.ScramSha512,
                _ => Confluent.Kafka.SaslMechanism.Plain,
            };
            config.SaslUsername = SaslUsername;
            config.SaslPassword = SaslPassword;
        }

        return config;
    }

    internal ConsumerConfig ToConsumerConfig(string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            ClientId = ClientId,
            GroupId = string.IsNullOrWhiteSpace(groupId) ? "swapmid-cdc-consumer" : groupId.Trim(),
            SecurityProtocol = SecurityProtocol,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            SocketTimeoutMs = 10_000,
        };

        if (RequiresSasl)
        {
            config.SaslMechanism = SaslMechanism switch
            {
                "ScramSha256" => Confluent.Kafka.SaslMechanism.ScramSha256,
                "ScramSha512" => Confluent.Kafka.SaslMechanism.ScramSha512,
                _ => Confluent.Kafka.SaslMechanism.Plain,
            };
            config.SaslUsername = SaslUsername;
            config.SaslPassword = SaslPassword;
        }

        return config;
    }

    internal string ToJson(bool includePassword)
    {
        var json = new JsonObject
        {
            ["provider"] = "kafka",
            ["bootstrapServers"] = BootstrapServers,
            ["clientId"] = ClientId,
            ["securityProtocol"] = SecurityProtocol.ToString(),
            ["acks"] = Acks,
            ["compressionType"] = CompressionType,
        };
        if (RequiresSasl)
        {
            json["saslMechanism"] = SaslMechanism;
            json["saslUsername"] = SaslUsername;
        }

        if (!string.IsNullOrWhiteSpace(TargetResourceGroup))
        {
            json["targetResourceGroup"] = TargetResourceGroup;
        }

        if (includePassword && RequiresSasl && !string.IsNullOrWhiteSpace(SaslPassword))
        {
            json["saslPassword"] = SaslPassword;
        }

        return json.ToJsonString();
    }
}
