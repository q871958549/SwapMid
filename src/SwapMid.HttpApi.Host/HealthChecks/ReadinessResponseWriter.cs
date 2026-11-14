using System.Text.Json;
using System.Threading.Tasks;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SwapMid.HealthChecks;

internal static class ReadinessResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        if (OperationsNetwork.IsPrivateOrLoopback(context.Connection.RemoteIpAddress))
        {
            return UIResponseWriter.WriteHealthCheckUIResponse(context, report);
        }

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
        }));
    }
}
