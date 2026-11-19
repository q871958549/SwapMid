using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SwapMid.Infrastructure;
using SwapMid.Monitoring;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// CNCF OpenLineage 2.0 异步事件推送器（严格 Fail-Closed：默认 <c>OpenLineage:Enabled=false</c>）：
/// 在 WAP 分支快进发布（promote）与原子时间旅行回滚（rollback）等 Lakehouse 生命周期里程碑，
/// 通过 <see cref="DataIntegrationSinkEndpointPolicy"/> SSRF 守卫向 Marquez / DataHub / OpenMetadata 推送
/// 标准 OpenLineage <c>v2-0-2</c> RunEvent；任何外网异常均为非阻塞 Best-Effort，绝不回滚已提交的湖仓事务。
/// </summary>
public sealed class OpenLineageEventEmitter : ITransientDependency
{
    public const string ProducerUri = "https://github.com/q871958549/SwapMid";
    public const string SpecSchemaUrl = "https://openlineage.io/spec/2-0-2/OpenLineage.json#/$defs/RunEvent";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenLineageEventEmitter> _logger;

    public OpenLineageEventEmitter(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OpenLineageEventEmitter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> TryEmitLakehouseLifecycleEventAsync(
        string operation,
        CdcStreamDefinition stream,
        string destination,
        long snapshotVersion,
        long snapshotId,
        long metadataVersion,
        string? refName,
        CancellationToken cancellationToken = default)
    {
        if (!bool.TryParse(_configuration["OpenLineage:Enabled"], out var enabled) || !enabled)
        {
            return false;
        }

        var endpointRaw = _configuration["OpenLineage:EndpointUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(endpointRaw) || !Uri.TryCreate(endpointRaw, UriKind.Absolute, out var endpointUri))
        {
            PlatformMetrics.OpenLineageEventsTotal.WithLabels(operation, "skipped").Inc();
            return false;
        }

        try
        {
            await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(endpointUri, _configuration, cancellationToken);

            var @namespace = string.IsNullOrWhiteSpace(_configuration["OpenLineage:Namespace"])
                ? "swapmid"
                : _configuration["OpenLineage:Namespace"]!.Trim();
            var runId = Guid.NewGuid().ToString("D");
            var payload = new
            {
                eventType = "COMPLETE",
                eventTime = DateTime.UtcNow.ToString("O"),
                producer = ProducerUri,
                schemaURL = SpecSchemaUrl,
                run = new
                {
                    runId,
                    facets = new
                    {
                        swapmidLakehouseLifecycle = new
                        {
                            _producer = ProducerUri,
                            _schemaURL = SpecSchemaUrl,
                            operation,
                            streamId = stream.Id,
                            streamName = stream.Name,
                            snapshotVersion,
                            snapshotId,
                            metadataVersion,
                            refName = refName ?? "main",
                        },
                    },
                },
                job = new
                {
                    @namespace,
                    name = $"cdc.{stream.Name}.{operation}",
                },
                inputs = new[]
                {
                    new
                    {
                        @namespace = $"{@namespace}:source",
                        name = string.IsNullOrWhiteSpace(stream.MonitoredTables) ? stream.Name : stream.MonitoredTables,
                    },
                },
                outputs = new[]
                {
                    new
                    {
                        @namespace = $"{@namespace}:lakehouse",
                        name = destination,
                    },
                },
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, endpointUri)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
            };
            var apiKey = _configuration["OpenLineage:ApiKey"]?.Trim();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            }

            var client = _httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                PlatformMetrics.OpenLineageEventsTotal.WithLabels(operation, "succeeded").Inc();
                return true;
            }

            PlatformMetrics.OpenLineageEventsTotal.WithLabels(operation, "failed").Inc();
            _logger.LogWarning(
                "OpenLineage lifecycle event rejected operation={Operation} statusCode={StatusCode}",
                operation,
                (int)resp.StatusCode);
            return false;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            PlatformMetrics.OpenLineageEventsTotal.WithLabels(operation, "failed").Inc();
            _logger.LogWarning(
                "OpenLineage lifecycle event emission skipped operation={Operation} errorType={ErrorType}",
                operation,
                ex.GetType().Name);
            return false;
        }
    }
}
