using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SwapMid.Monitoring;

/// <summary>
/// 将 ASP.NET Core / OpenTelemetry 当前 W3C TraceContext 的 <c>TraceId</c> 透传至响应头 <c>X-SwapMid-Trace-Id</c>，
/// 配合 Next.js BFF <c>traceparent</c> 转发实现浏览器 -> BFF -> .NET API -> 下游/日志全链路排障关联。
/// </summary>
public sealed class TraceContextResponseMiddleware
{
    public const string TraceIdHeaderName = "X-SwapMid-Trace-Id";
    private readonly RequestDelegate _next;

    public TraceContextResponseMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var traceId = Activity.Current?.TraceId.ToString();
            if (!string.IsNullOrWhiteSpace(traceId) &&
                !string.Equals(traceId, "00000000000000000000000000000000", System.StringComparison.Ordinal) &&
                !context.Response.Headers.ContainsKey(TraceIdHeaderName))
            {
                context.Response.Headers[TraceIdHeaderName] = traceId;
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
