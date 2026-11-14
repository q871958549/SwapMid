using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SwapMid.HttpApi.Versioning;

/// <summary>把 <see cref="ApiDeprecationAttribute"/> 转成调用方可观察的标准响应头。</summary>
public sealed class ApiDeprecationHeaderFilter : IAsyncResultFilter, IOrderedFilter
{
    public int Order => int.MinValue + 100;

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var deprecation = context.ActionDescriptor.EndpointMetadata
            .OfType<ApiDeprecationAttribute>()
            .LastOrDefault();

        if (deprecation != null)
        {
            var headers = context.HttpContext.Response.Headers;
            headers["Deprecation"] = deprecation.DeprecationHeaderValue;
            headers.Append("Link", $"<{deprecation.DocumentationUri}>; rel=\"deprecation\"");

            if (deprecation.SunsetAt is { } sunsetAt)
            {
                headers["Sunset"] = sunsetAt.ToString("R", CultureInfo.InvariantCulture);
            }
        }

        await next();
    }
}
