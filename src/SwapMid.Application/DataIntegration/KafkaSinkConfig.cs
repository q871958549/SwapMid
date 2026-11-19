using System.Collections.Generic;
using System.Text.Json.Nodes;
using Confluent.Kafka;

namespace SwapMid.DataIntegration;

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
    string TargetResourceGroup,
    string? SchemaRegistryUrl = null,
    string? SchemaRegistryUsername = null,
    string? SchemaRegistryPassword = null)
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

        if (!string.IsNullOrWhiteSpace(SchemaRegistryUrl))
        {
            json["schemaRegistryUrl"] = SchemaRegistryUrl;
            if (!string.IsNullOrWhiteSpace(SchemaRegistryUsername))
            {
                json["schemaRegistryUsername"] = SchemaRegistryUsername;
            }
        }

        if (includePassword && RequiresSasl && !string.IsNullOrWhiteSpace(SaslPassword))
        {
            json["saslPassword"] = SaslPassword;
        }

        if (includePassword && !string.IsNullOrWhiteSpace(SchemaRegistryPassword))
        {
            json["schemaRegistryPassword"] = SchemaRegistryPassword;
        }

        return json.ToJsonString();
    }
}
