using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Shouldly;

namespace SwapMid.HttpApi.Tests.Swagger;

public class ApiVersionRoutePolicyTests
{
    private static readonly Regex VersionedApiRoute = new(
        @"^api/v[1-9]\d*(?:/|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Public_custom_api_routes_must_use_path_major_version()
    {
        var controllerAssemblies = new[]
        {
            typeof(SwapMidHttpApiModule).Assembly,
            typeof(SwapMidHttpApiHostModule).Assembly,
        };

        var unversionedRoutes = controllerAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .Where(type => type.GetCustomAttributes(typeof(ApiExplorerSettingsAttribute), inherit: true)
                .Cast<ApiExplorerSettingsAttribute>()
                .All(attribute => !attribute.IgnoreApi))
            .SelectMany(type => type
                .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
                .Cast<RouteAttribute>()
                .Select(attribute => new
                {
                    Controller = type.FullName!,
                    Route = attribute.Template ?? string.Empty,
                }))
            .Where(item => item.Route.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            .Where(item => !VersionedApiRoute.IsMatch(item.Route))
            .Select(item => $"{item.Controller}|{item.Route}")
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        // 冻结的历史例外：平台内部 OTA 管理页已有前端消费者；只保留，不允许继续扩散。
        unversionedRoutes.ShouldBeEmpty();
    }
}
