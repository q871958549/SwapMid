using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SwapMid.Infrastructure;
using SwapMid.Monitoring;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// 受 SSRF 守卫保护的 Confluent / Redpanda Schema Registry 客户端：
/// 支持按 Confluent Wire Format（<c>0x00</c> + 4 字节大端 Schema ID）拉取与缓存 Avro Schema，
/// 并将二进制 Avro Debezium 信封转换为标准 <see cref="PgOutputCapturedChange"/>。
/// </summary>
internal static class ConfluentSchemaRegistryClient
{
    private const int MaxCachedSchemas = 256;
    private static readonly ConcurrentDictionary<string, string> SchemaCache = new(StringComparer.Ordinal);

    internal static bool IsConfluentWirePayload(byte[]? raw)
        => raw is { Length: >= 5 } && raw[0] == 0x00;

    internal static async Task<(bool Reachable, int SubjectCount, string Message)> ProbeAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        KafkaSinkConfig kafkaConfig,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(kafkaConfig.SchemaRegistryUrl))
        {
            return (true, 0, "未配置 Schema Registry（按 JSON 模式消费）。");
        }

        var probeUri = new Uri($"{kafkaConfig.SchemaRegistryUrl.TrimEnd('/')}/subjects", UriKind.Absolute);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(probeUri, configuration, cancellationToken);

        using var req = BuildAuthorizedGet(probeUri, kafkaConfig);
        var client = httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            return (false, 0, $"Schema Registry 探测返回 HTTP {(int)resp.StatusCode}。");
        }

        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        var count = 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                count = doc.RootElement.GetArrayLength();
            }
        }
        catch (JsonException)
        {
            // Keep count = 0 if body is non-array JSON.
        }

        return (true, count, $"Schema Registry 已就绪（已注册 {count} 个 Subject）。");
    }

    internal static async Task<string> FetchAvroSchemaAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        KafkaSinkConfig kafkaConfig,
        int schemaId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(kafkaConfig.SchemaRegistryUrl))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"检测到 Confluent Wire Format 二进制消息（Schema ID {schemaId}），但绑定的 Kafka 数据源未配置 schemaRegistryUrl。");
        }

        var baseUrl = kafkaConfig.SchemaRegistryUrl.TrimEnd('/');
        var cacheKey = $"{baseUrl}|{schemaId.ToString(CultureInfo.InvariantCulture)}";
        if (SchemaCache.TryGetValue(cacheKey, out var cached))
        {
            PlatformMetrics.CdcSchemaRegistryFetchesTotal.WithLabels("cache_hit").Inc();
            return cached;
        }

        var uri = new Uri($"{baseUrl}/schemas/ids/{schemaId.ToString(CultureInfo.InvariantCulture)}", UriKind.Absolute);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(uri, configuration, cancellationToken);

        using var req = BuildAuthorizedGet(uri, kafkaConfig);
        var client = httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            PlatformMetrics.CdcSchemaRegistryFetchesTotal.WithLabels("failed").Inc();
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                .WithData("Reason", $"从 Schema Registry 拉取 Schema ID {schemaId} 失败（HTTP {(int)resp.StatusCode}）。");
        }

        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("schemaType", out var stProp) &&
            stProp.ValueKind == JsonValueKind.String &&
            !string.Equals(stProp.GetString(), "AVRO", StringComparison.OrdinalIgnoreCase))
        {
            PlatformMetrics.CdcSchemaRegistryFetchesTotal.WithLabels("failed").Inc();
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"Schema ID {schemaId} 类型为 {stProp.GetString()}，当前二进制解码仅支持 AVRO。");
        }

        if (!doc.RootElement.TryGetProperty("schema", out var schemaProp) ||
            schemaProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(schemaProp.GetString()))
        {
            PlatformMetrics.CdcSchemaRegistryFetchesTotal.WithLabels("failed").Inc();
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                .WithData("Reason", $"Schema Registry 返回的 Schema ID {schemaId} 缺少有效 schema 定义。");
        }

        var avroSchemaJson = schemaProp.GetString()!;
        if (SchemaCache.Count >= MaxCachedSchemas)
        {
            SchemaCache.Clear();
        }

        SchemaCache[cacheKey] = avroSchemaJson;
        PlatformMetrics.CdcSchemaRegistryFetchesTotal.WithLabels("fetched").Inc();
        return avroSchemaJson;
    }

    internal static async Task<PgOutputCapturedChange?> TryDecodeConfluentAvroMessageAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        KafkaSinkConfig kafkaConfig,
        byte[] rawPayload,
        string topic,
        int partition,
        long offset,
        DateTime fallbackTimestampUtc,
        CancellationToken cancellationToken)
    {
        if (!IsConfluentWirePayload(rawPayload))
        {
            return null;
        }

        var schemaId = BinaryPrimitives.ReadInt32BigEndian(rawPayload.AsSpan(1, 4));
        if (schemaId <= 0)
        {
            return null;
        }

        var avroSchemaJson = await FetchAvroSchemaAsync(
            httpClientFactory, configuration, kafkaConfig, schemaId, cancellationToken);
        var decoded = AvroBinaryDatumReader.Decode(avroSchemaJson, rawPayload.AsSpan(5));
        if (decoded == null)
        {
            return null;
        }

        var normalizedJson = JsonSerializer.Serialize(decoded);
        return KafkaDebeziumEnvelopeParser.TryParseMessage(
            normalizedJson, topic, partition, offset, fallbackTimestampUtc, out var change)
            ? change
            : null;
    }

    internal static void ClearCacheForTesting() => SchemaCache.Clear();

    private static HttpRequestMessage BuildAuthorizedGet(Uri uri, KafkaSinkConfig cfg)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.schemaregistry.v1+json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(cfg.SchemaRegistryUsername))
        {
            var token = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{cfg.SchemaRegistryUsername}:{cfg.SchemaRegistryPassword ?? string.Empty}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        return req;
    }
}
