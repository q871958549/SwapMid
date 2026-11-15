using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.ApiManagement;
using SwapMid.Monitoring;
using SwapMid.SharedKernel.IntegrationEvents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Uow;

namespace SwapMid.ApiGateway;

/// <summary>
/// API 网关异步事件接入入口（<c>POST /api/gateway/events/{topicName}</c>）：
/// 接收外部合作伙伴的 JSON 事件并桥接进入 <c>MessageBus</c>，立即返回 <c>202 Accepted</c>，支持 <c>Idempotency-Key</c> 幂等去重。
/// </summary>
[Authorize(GatewayAuthorizationPolicies.GatewayAccessPolicy)]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/gateway/events")]
public class GatewayEventsController : AbpController
{
    public const long MaxEventPayloadBytes = 256L * 1024;

    private static readonly Regex TopicNamePattern =
        new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.Compiled);

    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public GatewayEventsController(
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    [UnitOfWork(IsDisabled = true)]
    [HttpPost("{topicName}", Order = -1)]
    public async Task<IActionResult> IngestAsync(string topicName, CancellationToken cancellationToken)
    {
        if (!bool.TryParse(_configuration["ApiGateway:Enabled"], out var enabled) || !enabled)
        {
            return await WriteJsonAsync(
                StatusCodes.Status404NotFound,
                new { error = new { code = "SwapMid:GW:Disabled", message = "Not Found" } },
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(topicName) || !TopicNamePattern.IsMatch(topicName))
        {
            PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("invalid_event", "4xx").Inc();
            return await WriteJsonAsync(
                StatusCodes.Status400BadRequest,
                new { error = new { code = "SwapMid:GW:InvalidTopicName", message = "事件主题名称格式非法" } },
                cancellationToken);
        }

        var externalClientId = GatewayAuthorizationPolicies.ExtractExternalClientId(User);
        GatewayClientPolicySnapshot? clientPolicy = null;
        if (externalClientId != null)
        {
            var governanceReader = _serviceProvider.GetService<IGatewayClientGovernanceReader>();
            if (governanceReader != null)
            {
                clientPolicy = await governanceReader.FindPolicyAsync(externalClientId, cancellationToken);
            }
        }

        var defaultPermit = _configuration.GetValue<int?>("ApiGateway:DefaultRateLimitPerMinute")
            ?? GatewayController.DefaultRateLimitPerMinute;
        var effectivePermit = clientPolicy?.RateLimitPerMinute ?? defaultPermit;
        var rateLimiter = _serviceProvider.GetService<IGatewayRateLimiter>();
        if (rateLimiter != null)
        {
            var partitionKey = GatewayController.BuildPartitionKey(externalClientId, User, HttpContext);
            var lease = rateLimiter.TryAcquire(partitionKey, effectivePermit);
            if (!lease.Allowed)
            {
                PlatformMetrics.OutRateLimitRejections.WithLabels("api_gateway", "event").Inc();
                PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("rate_limited", "4xx").Inc();
                Response.Headers.RetryAfter = lease.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                return await WriteJsonAsync(
                    StatusCodes.Status429TooManyRequests,
                    new { error = new { code = "SwapMid:GW:RateLimited", message = "事件接入速率超出配额" } },
                    cancellationToken);
            }
        }

        if (Request.ContentLength is { } contentLength && contentLength > MaxEventPayloadBytes)
        {
            PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("payload_too_large", "4xx").Inc();
            return await WriteJsonAsync(
                StatusCodes.Status413PayloadTooLarge,
                new { error = new { code = "SwapMid:GW:EventTooLarge", message = "事件载荷超出 256 KiB 上限" } },
                cancellationToken);
        }

        var bytes = await GatewayController.ReadBoundedStreamAsync(
            Request.Body,
            MaxEventPayloadBytes,
            cancellationToken);
        if (bytes is null)
        {
            PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("payload_too_large", "4xx").Inc();
            return await WriteJsonAsync(
                StatusCodes.Status413PayloadTooLarge,
                new { error = new { code = "SwapMid:GW:EventTooLarge", message = "事件载荷超出 256 KiB 上限" } },
                cancellationToken);
        }

        if (bytes.Length == 0)
        {
            PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("invalid_event", "4xx").Inc();
            return await WriteJsonAsync(
                StatusCodes.Status400BadRequest,
                new { error = new { code = "SwapMid:GW:EmptyEventPayload", message = "事件 JSON 正文不能为空" } },
                cancellationToken);
        }

        string payloadJson;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            payloadJson = Encoding.UTF8.GetString(bytes);
        }
        catch (JsonException)
        {
            PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("invalid_event", "4xx").Inc();
            return await WriteJsonAsync(
                StatusCodes.Status400BadRequest,
                new { error = new { code = "SwapMid:GW:MalformedEventJson", message = "事件正文必须是合法 JSON" } },
                cancellationToken);
        }

        var publisher = _serviceProvider.GetService<IIntegrationEventPublisher>();
        if (publisher is null)
        {
            PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("publisher_unavailable", "5xx").Inc();
            return await WriteJsonAsync(
                StatusCodes.Status503ServiceUnavailable,
                new { error = new { code = "SwapMid:GW:EventBusUnavailable", message = "消息总线未就绪" } },
                cancellationToken);
        }

        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString().Trim();
        var eventId = string.IsNullOrEmpty(idempotencyKey)
            ? Guid.NewGuid()
            : DeterministicEventId.Create(
                $"gateway:{externalClientId ?? "user"}:{topicName}:{idempotencyKey}");

        var rawSubject = Request.Headers["X-SwapMid-Subject"].ToString().Trim();
        if (string.IsNullOrEmpty(rawSubject))
        {
            rawSubject = Request.Headers["ce-subject"].ToString().Trim();
        }

        var subject = string.IsNullOrEmpty(rawSubject)
            ? $"gateway.ingress:{topicName}"
            : rawSubject;

        var envelope = new IntegrationEventEnvelope(
            EventId: eventId,
            TopicName: topicName,
            Subject: subject,
            PayloadJson: payloadJson);

        var published = await publisher.PublishIfTopicActiveAsync(envelope, cancellationToken);
        if (!published)
        {
            PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("topic_not_found", "4xx").Inc();
            return await WriteJsonAsync(
                StatusCodes.Status404NotFound,
                new { error = new { code = "SwapMid:GW:TopicNotActive", message = $"目标消息主题不存在或未启用：{topicName}" } },
                cancellationToken);
        }

        PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("event_accepted", "2xx").Inc();
        return await WriteJsonAsync(
            StatusCodes.Status202Accepted,
            new
            {
                eventId,
                topicName,
                status = "accepted",
            },
            cancellationToken);
    }

    private async Task<IActionResult> WriteJsonAsync(
        int statusCode,
        object payload,
        CancellationToken cancellationToken)
    {
        Response.StatusCode = statusCode;
        Response.ContentType = "application/json; charset=utf-8";
        await Response.WriteAsync(JsonSerializer.Serialize(payload), cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
        return new EmptyResult();
    }
}
