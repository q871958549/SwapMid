using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.ApiManagement;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.AspNetCore.Mvc;

namespace SwapMid.ApiGateway;

/// <summary>
/// API 网关休眠骨架：按「API 管理」中的路径、方法和上游地址转发请求。
/// 默认由 <c>ApiGateway:Enabled=false</c> 关闭；当前实现不得视为安全反向代理。
/// 在完成 SSRF/DNS 重绑定防护、头与重定向策略、资源边界和短事务拆分前禁止启用。
/// </summary>
[Authorize(SwapMidPermissions.ApiManagement.ApiRoutes.Default)]
[ApiExplorerSettings(IgnoreApi = true)]   // 网关入口不进 Swagger API 文档
[Route("api/gateway")]
public class GatewayController : AbpController
{
    /// <summary>逐跳(hop-by-hop)头 + Host,不在代理两端透传(RFC 9110 §7.6.1)。</summary>
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host",
    };

    private readonly IApiRouteRepository _routes;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public GatewayController(
        IApiRouteRepository routes,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _routes = routes;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [AcceptVerbs("GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS")]
    [Route("{**path}")]
    public async Task ForwardAsync(string? path, CancellationToken cancellationToken)
    {
        if (!bool.TryParse(_configuration["ApiGateway:Enabled"], out var enabled) || !enabled)
        {
            await WriteErrorAsync(StatusCodes.Status404NotFound, "Not Found", cancellationToken);
            return;
        }

        var incomingPath = "/" + (path ?? string.Empty).TrimStart('/');
        var method = Enum.TryParse<HttpMethodType>(Request.Method, ignoreCase: true, out var m)
            ? m
            : HttpMethodType.ALL;   // HEAD/OPTIONS 等未枚举的方法只命中 ALL 通配路由

        var route = await _routes.FindEnabledMatchAsync(incomingPath, method, cancellationToken);
        if (route is null)
        {
            await WriteErrorAsync(StatusCodes.Status404NotFound,
                $"无匹配的网关路由：{Request.Method} {incomingPath}（请在「API 管理 → 路由」配置并启用对应路由）",
                cancellationToken);
            return;
        }

        using var proxyRequest = new HttpRequestMessage(
            new HttpMethod(Request.Method),
            route.UpstreamUrl + Request.QueryString.Value);

        // 请求体透传(GET/HEAD 一般无体)
        if (Request.ContentLength is > 0 || Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            proxyRequest.Content = new StreamContent(Request.Body);
        }

        // 头透传:实体头(Content-*)归 Content.Headers,其余归 Request.Headers;剔除逐跳头与 Host
        foreach (var header in Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key))
            {
                continue;
            }

            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                proxyRequest.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
            else
            {
                proxyRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        var client = _httpClientFactory.CreateClient("ApiGateway");
        HttpResponseMessage upstream;
        try
        {
            upstream = await client.SendAsync(proxyRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "API 网关转发失败 {Method} {Path} → {Upstream}",
                Request.Method, incomingPath, route.UpstreamUrl);
            await WriteErrorAsync(StatusCodes.Status502BadGateway,
                $"上游服务不可达：{route.UpstreamUrl}（{ex.Message}）", cancellationToken);
            return;
        }

        using (upstream)
        {
            Response.StatusCode = (int)upstream.StatusCode;

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

            // Transfer-Encoding 交给 Kestrel 决定,避免与实际响应体冲突
            Response.Headers.Remove("transfer-encoding");

            await upstream.Content.CopyToAsync(Response.Body, cancellationToken);
        }
    }

    private async Task WriteErrorAsync(int statusCode, string message, CancellationToken cancellationToken)
    {
        Response.StatusCode = statusCode;
        Response.ContentType = "application/json; charset=utf-8";
        await Response.WriteAsync(
            JsonSerializer.Serialize(new { error = new { code = statusCode, message } }),
            cancellationToken);
    }
}
