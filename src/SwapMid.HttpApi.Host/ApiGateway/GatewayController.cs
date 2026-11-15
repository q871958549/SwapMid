using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.ApiManagement;
using SwapMid.Infrastructure;
using SwapMid.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Uow;

namespace SwapMid.ApiGateway;

/// <summary>
/// 生产加固型受管 API 网关：
/// 1. 默认由 <c>ApiGateway:Enabled=false</c> 关闭（Fail-Closed）；
/// 2. 支持平台权限与第三方 <c>client_credentials</c>（<c>external-integration</c>）双主体鉴权；
/// 3. 强制执行客户端路由分组 ACL、滑动窗口动态限流、载荷预算上限、SSRF / DNS Rebinding 防护与无环境事务转发。
/// </summary>
[Authorize(GatewayAuthorizationPolicies.GatewayAccessPolicy)]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/gateway")]
public class GatewayController : AbpController
{
    public const int DefaultRateLimitPerMinute = 120;
    public const long DefaultMaxPayloadBytes = 10L * 1024 * 1024;

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host",
    };

    private static readonly HashSet<string> SensitiveInboundHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie",
    };

    private readonly IApiRouteRepository _routes;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GatewayController> _logger;

    public GatewayController(
        IApiRouteRepository routes,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<GatewayController> logger)
    {
        _routes = routes;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    [UnitOfWork(IsDisabled = true)]
    [AcceptVerbs("GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS")]
    [Route("{**path}")]
    public async Task<IActionResult> ForwardAsync(string? path, CancellationToken cancellationToken)
    {
        if (!bool.TryParse(_configuration["ApiGateway:Enabled"], out var enabled) || !enabled)
        {
            return await WriteErrorAsync(StatusCodes.Status404NotFound, "SwapMid:GW:Disabled", "Not Found", cancellationToken);
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

        var defaultPermit = _configuration.GetValue<int?>("ApiGateway:DefaultRateLimitPerMinute") ?? DefaultRateLimitPerMinute;
        var effectivePermit = clientPolicy?.RateLimitPerMinute ?? defaultPermit;
        var rateLimiter = _serviceProvider.GetService<IGatewayRateLimiter>();
        if (rateLimiter != null)
        {
            var partitionKey = BuildPartitionKey(externalClientId, User, HttpContext);
            var lease = rateLimiter.TryAcquire(partitionKey, effectivePermit);
            if (!lease.Allowed)
            {
                PlatformMetrics.OutRateLimitRejections.WithLabels("api_gateway", "request").Inc();
                PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("rate_limited", "4xx").Inc();
                Response.Headers.RetryAfter = lease.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                return await WriteErrorAsync(
                    StatusCodes.Status429TooManyRequests,
                    "SwapMid:GW:RateLimited",
                    "网关调用速率超出客户端配额，请按 Retry-After 稍后重试",
                    cancellationToken);
            }
        }

        var maxPayloadBytes = _configuration.GetValue<long?>("ApiGateway:MaxPayloadBytes") ?? DefaultMaxPayloadBytes;
        if (Request.ContentLength is { } declaredLength && declaredLength > maxPayloadBytes)
        {
            PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("payload_too_large", "4xx").Inc();
            return await WriteErrorAsync(
                StatusCodes.Status413PayloadTooLarge,
                "SwapMid:GW:PayloadTooLarge",
                "网关请求体超出允许上限",
                cancellationToken);
        }

        var incomingPath = "/" + (path ?? string.Empty).TrimStart('/');
        var method = Enum.TryParse<HttpMethodType>(Request.Method, ignoreCase: true, out var parsedMethod)
            ? parsedMethod
            : HttpMethodType.ALL;

        var route = await FindEnabledRouteShortAsync(incomingPath, method, cancellationToken);
        if (route is null)
        {
            PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("route_not_found", "4xx").Inc();
            return await WriteErrorAsync(
                StatusCodes.Status404NotFound,
                "SwapMid:GW:RouteNotFound",
                $"无匹配的网关路由：{Request.Method} {incomingPath}",
                cancellationToken);
        }

        if (clientPolicy is { AllowedGroupIds.Count: > 0 } &&
            (route.GroupId is not { } routeGroupId || !clientPolicy.AllowedGroupIds.Contains(routeGroupId)))
        {
            PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("acl_denied", "4xx").Inc();
            return await WriteErrorAsync(
                StatusCodes.Status403Forbidden,
                "SwapMid:GW:GroupAccessDenied",
                "当前客户端未被授权访问该 API 路由所属分组",
                cancellationToken);
        }

        var targetUrl = route.UpstreamUrl + Request.QueryString.Value;
        if (!ApiGatewayUpstreamEndpointPolicy.TryValidateUpstreamUrl(
                targetUrl,
                _configuration,
                out var validatedUri,
                out var failureCode))
        {
            PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("ssrf_blocked", "5xx").Inc();
            _logger.LogWarning(
                "API 网关拦截未授权上游地址 routeId={RouteId} code={FailureCode}",
                route.Id,
                failureCode);
            return await WriteErrorAsync(
                StatusCodes.Status502BadGateway,
                failureCode,
                "上游地址未通过网关安全白名单校验",
                cancellationToken);
        }

        byte[]? bufferedRequestBody = null;
        if (Request.ContentLength is > 0 || Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            bufferedRequestBody = await ReadBoundedStreamAsync(Request.Body, maxPayloadBytes, cancellationToken);
            if (bufferedRequestBody is null)
            {
                PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("payload_too_large", "4xx").Inc();
                return await WriteErrorAsync(
                    StatusCodes.Status413PayloadTooLarge,
                    "SwapMid:GW:PayloadTooLarge",
                    "网关请求体超出允许上限",
                    cancellationToken);
            }
        }

        using var proxyRequest = new HttpRequestMessage(new HttpMethod(Request.Method), validatedUri);
        if (bufferedRequestBody is not null)
        {
            proxyRequest.Content = new ByteArrayContent(bufferedRequestBody);
        }

        var forwardCredentials = _configuration.GetValue<bool>("ApiGateway:ForwardCredentials");
        foreach (var header in Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key) ||
                (!forwardCredentials && SensitiveInboundHeaders.Contains(header.Key)))
            {
                continue;
            }

            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    proxyRequest.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }
            else
            {
                proxyRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        proxyRequest.Headers.TryAddWithoutValidation("X-SwapMid-Gateway-Route", route.Id.ToString("D"));
        if (externalClientId != null)
        {
            proxyRequest.Headers.TryAddWithoutValidation("X-SwapMid-Client-Id", externalClientId);
        }

        var client = _httpClientFactory.CreateClient(ExternalHttpClientNames.ApiGateway);
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage upstream;
        try
        {
            upstream = await client.SendAsync(
                proxyRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            PlatformMetrics.ApiGatewayUpstreamDuration.Observe(stopwatch.Elapsed.TotalSeconds);
            PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("upstream_error", "5xx").Inc();
            _logger.LogWarning(
                "API 网关转发失败 method={Method} path={Path} routeId={RouteId} exceptionType={ExceptionType}",
                Request.Method,
                incomingPath,
                route.Id,
                ex.GetType().Name);
            return await WriteErrorAsync(
                StatusCodes.Status502BadGateway,
                "SwapMid:GW:UpstreamUnavailable",
                "上游服务不可达或未通过建连安全护栏",
                cancellationToken);
        }

        using (upstream)
        {
            if (upstream.Content.Headers.ContentLength is { } upstreamLength &&
                upstreamLength > maxPayloadBytes)
            {
                stopwatch.Stop();
                PlatformMetrics.ApiGatewayUpstreamDuration.Observe(stopwatch.Elapsed.TotalSeconds);
                PlatformMetrics.ApiGatewayRequestsTotal.WithLabels("upstream_too_large", "5xx").Inc();
                return await WriteErrorAsync(
                    StatusCodes.Status502BadGateway,
                    "SwapMid:GW:UpstreamResponseTooLarge",
                    "上游响应体超出网关安全预算",
                    cancellationToken);
            }

            var statusCode = (int)upstream.StatusCode;
            Response.StatusCode = statusCode;

            foreach (var header in upstream.Headers)
            {
                if (!HopByHopHeaders.Contains(header.Key))
                {
                    Response.Headers[header.Key] = header.Value.ToArray();
                }
            }

            foreach (var header in upstream.Content.Headers)
            {
                if (!HopByHopHeaders.Contains(header.Key))
                {
                    Response.Headers[header.Key] = header.Value.ToArray();
                }
            }

            Response.Headers.Remove("transfer-encoding");
            await CopyBoundedAsync(upstream.Content, Response.Body, maxPayloadBytes, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            stopwatch.Stop();
            PlatformMetrics.ApiGatewayUpstreamDuration.Observe(stopwatch.Elapsed.TotalSeconds);
            PlatformMetrics.ApiGatewayRequestsTotal
                .WithLabels("forwarded", $"{statusCode / 100}xx")
                .Inc();
            return new EmptyResult();
        }
    }

    private async Task<ApiRoute?> FindEnabledRouteShortAsync(
        string incomingPath,
        HttpMethodType method,
        CancellationToken cancellationToken)
    {
        var uowManager = _serviceProvider.GetService<IUnitOfWorkManager>();
        if (uowManager is null)
        {
            return await _routes.FindEnabledMatchAsync(incomingPath, method, cancellationToken);
        }

        using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
        var route = await _routes.FindEnabledMatchAsync(incomingPath, method, cancellationToken);
        await uow.CompleteAsync(cancellationToken);
        return route;
    }

    internal static string BuildPartitionKey(
        string? externalClientId,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        if (!string.IsNullOrWhiteSpace(externalClientId))
        {
            return $"client:{externalClientId}";
        }

        var subject = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(subject))
        {
            return $"user:{subject}";
        }

        return $"ip:{httpContext.Connection.RemoteIpAddress}";
    }

    internal static async Task<byte[]?> ReadBoundedStreamAsync(
        Stream stream,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var ms = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    return null;
                }

                ms.Write(buffer, 0, read);
            }

            return ms.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task CopyBoundedAsync(
        HttpContent content,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<IActionResult> WriteErrorAsync(
        int statusCode,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        Response.StatusCode = statusCode;
        Response.ContentType = "application/json; charset=utf-8";
        await Response.WriteAsync(
            JsonSerializer.Serialize(new { error = new { code, message } }),
            cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
        return new EmptyResult();
    }
}
