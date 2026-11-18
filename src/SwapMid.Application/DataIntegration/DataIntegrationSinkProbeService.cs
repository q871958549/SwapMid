using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using SwapMid.Infrastructure;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// ClickHouse / ObjectStorage / Kafka 目标源连通性探测与 ClickHouse system.columns 元数据自省。
/// </summary>
public class DataIntegrationSinkProbeService : ITransientDependency
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public DataIntegrationSinkProbeService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    internal Task<string?> TryReadObjectStorageJsonAsync(
        ObjectStorageSinkConfig config,
        string objectKey,
        CancellationToken cancellationToken)
        => CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
            _httpClientFactory,
            _configuration,
            config,
            objectKey,
            cancellationToken);

    internal async Task<TestConnectionResultDto> TestConnectionAsync(
        DataSourceType type,
        string normalizedConfigJson,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            switch (type)
            {
                case DataSourceType.ClickHouse:
                    await ProbeClickHouseAsync(
                        ClickHouseSinkConfigContract.ParseForWrite(normalizedConfigJson),
                        cancellationToken);
                    stopwatch.Stop();
                    return new TestConnectionResultDto
                    {
                        Success = true,
                        Message = "ClickHouse HTTP 连接成功（库与凭据验证通过）",
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                    };

                case DataSourceType.ObjectStorage:
                    await ProbeObjectStorageAsync(
                        ObjectStorageSinkConfigContract.ParseForWrite(normalizedConfigJson, requireSecretKey: true),
                        cancellationToken);
                    stopwatch.Stop();
                    return new TestConnectionResultDto
                    {
                        Success = true,
                        Message = "对象存储（S3/MinIO）SigV4 签名与存储桶可达性验证通过",
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                    };

                case DataSourceType.Kafka:
                    var brokerCount = await ProbeKafkaAsync(
                        KafkaSinkConfigContract.ParseForWrite(normalizedConfigJson, requirePasswordIfSasl: true),
                        cancellationToken);
                    stopwatch.Stop();
                    return new TestConnectionResultDto
                    {
                        Success = true,
                        Message = $"Kafka 集群连接成功（发现 {brokerCount} 个 Broker）",
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                    };

                default:
                    stopwatch.Stop();
                    return new TestConnectionResultDto
                    {
                        Success = false,
                        Message = "不支持的连接器类型。",
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                    };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Volo.Abp.BusinessException)
        {
            stopwatch.Stop();
            return new TestConnectionResultDto
            {
                Success = false,
                Message = "连接被安全出站白名单拦截，请检查主机与 DataIntegration:Sinks:AllowedHosts / AllowedPrivateNetworks 配置。",
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorKind = ex.GetType().Name;
            return new TestConnectionResultDto
            {
                Success = false,
                Message = $"连接失败，请检查端点、网络策略与凭据配置（{errorKind}）。",
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            };
        }
    }

    internal async Task<List<PreflightColumnDto>> InspectClickHouseColumnsAsync(
        ClickHouseSinkConfig config,
        string tableDestination,
        CancellationToken cancellationToken)
    {
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
            config.BuildBaseUri(),
            _configuration,
            cancellationToken);

        var (schemaName, tableName) = SplitClickHouseTable(config.Database, tableDestination);
        var queryUri = new Uri(
            config.BuildBaseUri(),
            $"?database={Uri.EscapeDataString(config.Database)}");

        var sql =
            $"SELECT name, type, is_in_primary_key, default_kind FROM system.columns " +
            $"WHERE database = '{EscapeSqlLiteral(schemaName)}' AND table = '{EscapeSqlLiteral(tableName)}' " +
            "ORDER BY position FORMAT JSONCompact";

        using var request = CreateClickHousePostRequest(queryUri, config, sql);
        var client = _httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ClickHouse schema query failed ({(int)response.StatusCode}): {body}");
        }

        return ParseClickHouseSystemColumns(body);
    }

    internal async Task ExecuteClickHouseDdlAsync(
        ClickHouseSinkConfig config,
        string destination,
        string ddlSql,
        CancellationToken cancellationToken)
    {
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
            config.BuildBaseUri(),
            _configuration,
            cancellationToken);

        var (dbName, _) = SplitClickHouseTable(config.Database, destination);
        var escapedDb = dbName.Replace("`", "``", StringComparison.Ordinal);
        var baseUri = config.BuildBaseUri();
        var client = _httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);

        using (var createDbReq = CreateClickHousePostRequest(
            baseUri,
            config,
            $"CREATE DATABASE IF NOT EXISTS `{escapedDb}`"))
        using (var createDbRes = await client.SendAsync(createDbReq, HttpCompletionOption.ResponseContentRead, cancellationToken))
        {
            if (!createDbRes.IsSuccessStatusCode)
            {
                var err = await createDbRes.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"ClickHouse create database failed ({(int)createDbRes.StatusCode}): {TruncateError(err)}");
            }
        }

        var queryUri = new Uri(baseUri, $"?database={Uri.EscapeDataString(dbName)}");
        using var ddlReq = CreateClickHousePostRequest(queryUri, config, ddlSql);
        using var ddlRes = await client.SendAsync(ddlReq, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!ddlRes.IsSuccessStatusCode)
        {
            var err = await ddlRes.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"ClickHouse DDL execution failed ({(int)ddlRes.StatusCode}): {TruncateError(err)}");
        }
    }

    internal static HttpRequestMessage CreateClickHousePostRequest(
        Uri requestUri,
        ClickHouseSinkConfig config,
        string sqlBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(sqlBody, Encoding.UTF8, "text/plain"),
        };
        if (!string.IsNullOrWhiteSpace(config.Username))
        {
            request.Headers.TryAddWithoutValidation("X-ClickHouse-User", config.Username);
        }

        if (!string.IsNullOrEmpty(config.Password))
        {
            request.Headers.TryAddWithoutValidation("X-ClickHouse-Key", config.Password);
        }

        return request;
    }

    internal static (string Database, string Table) SplitClickHouseTable(string defaultDb, string destination)
    {
        var trimmed = destination.Trim();
        var dot = trimmed.IndexOf('.');
        return dot > 0
            ? (trimmed[..dot], trimmed[(dot + 1)..])
            : (defaultDb, trimmed);
    }

    private async Task ProbeClickHouseAsync(ClickHouseSinkConfig config, CancellationToken cancellationToken)
    {
        var baseUri = config.BuildBaseUri();
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
            baseUri,
            _configuration,
            cancellationToken);

        var queryUri = new Uri(baseUri, $"?database={Uri.EscapeDataString(config.Database)}");
        using var request = CreateClickHousePostRequest(queryUri, config, "SELECT 1 FORMAT TabSeparated");
        var client = _httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {TruncateError(payload)}");
        }
    }

    private async Task ProbeObjectStorageAsync(ObjectStorageSinkConfig config, CancellationToken cancellationToken)
    {
        var probeUri = config.BuildBucketProbeUri();
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
            probeUri,
            _configuration,
            cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, probeUri);
        AwsSigV4Signer.Sign(
            request,
            config.Region,
            config.AccessKey,
            config.SecretKey ?? string.Empty,
            AwsSigV4Signer.EmptyPayloadSha256Hex);

        var client = _httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"S3 HTTP {(int)response.StatusCode}: {TruncateError(body)}");
        }
    }

    private async Task<int> ProbeKafkaAsync(KafkaSinkConfig config, CancellationToken cancellationToken)
    {
        await DataIntegrationSinkEndpointPolicy.ValidateKafkaBrokersAsync(
            config.Brokers,
            _configuration,
            cancellationToken);

        return await Task.Run(() =>
        {
            using var admin = new AdminClientBuilder(config.ToAdminConfig()).Build();
            var metadata = admin.GetMetadata(TimeSpan.FromSeconds(8));
            if (metadata.Brokers.Count == 0)
            {
                throw new InvalidOperationException("Kafka 元数据中未发现可用 Broker。");
            }

            return metadata.Brokers.Count;
        }, cancellationToken);
    }

    private static List<PreflightColumnDto> ParseClickHouseSystemColumns(string jsonBody)
    {
        using var doc = JsonDocument.Parse(jsonBody);
        var columns = new List<PreflightColumnDto>();
        if (!doc.RootElement.TryGetProperty("data", out var dataElement) ||
            dataElement.ValueKind != JsonValueKind.Array)
        {
            return columns;
        }

        foreach (var row in dataElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 4)
            {
                continue;
            }

            var name = row[0].GetString() ?? string.Empty;
            var chType = row[1].GetString() ?? "String";
            var isPk = row[2].ValueKind == JsonValueKind.Number
                ? row[2].GetInt32() > 0
                : string.Equals(row[2].GetString(), "1", StringComparison.Ordinal);
            var defaultKind = row[3].GetString() ?? string.Empty;

            columns.Add(new PreflightColumnDto
            {
                Name = name,
                DataType = chType,
                IsNullable = chType.StartsWith("Nullable(", StringComparison.OrdinalIgnoreCase),
                IsPrimaryKey = isPk,
                IsUniqueIndexed = isPk,
                HasDefaultOrIdentity = !string.IsNullOrWhiteSpace(defaultKind),
            });
        }

        return columns;
    }

    private static string EscapeSqlLiteral(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private static string TruncateError(string value)
    {
        var clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= 240 ? clean : clean[..240];
    }
}
