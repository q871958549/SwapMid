using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SwapMid.HealthChecks;

public static class HealthChecksBuilderExtensions
{
    private const string LivenessPath = "/health/live";
    private const string TrafficReadyPath = "/health/ready";
    private const string ReadinessPath = "/health-status";

    public static void AddSwapMidHealthChecks(this IServiceCollection services)
    {
        var healthChecksBuilder = services.AddHealthChecks();
        healthChecksBuilder.AddCheck<SwapMidDatabaseCheck>("SwapMid DbContext Check", tags: new string[] { "infra", "database" });
        healthChecksBuilder.AddCheck<SwapMidRedisCheck>("Redis", tags: new string[] { "infra", "cache" });
        healthChecksBuilder.AddCheck<SwapMidRabbitMqCheck>("RabbitMQ", tags: new string[] { "infra", "mq" });

        services.ConfigureHealthCheckEndpoints();

        var configuration = services.GetConfiguration();
        var healthCheckUrl = configuration["App:HealthCheckUrl"];

        if (string.IsNullOrEmpty(healthCheckUrl))
        {
            healthCheckUrl = ReadinessPath;
        }

        if (!configuration.GetValue("App:EnableHealthChecksUI", false))
        {
            return;
        }

        var healthChecksUiBuilder = services.AddHealthChecksUI(settings =>
        {
            settings.AddHealthCheckEndpoint("SwapMid Health Status", configuration["App:HealthUiCheckUrl"] ?? healthCheckUrl);
        });

        healthChecksUiBuilder.AddInMemoryStorage();

        services.MapHealthChecksUiEndpoints(options =>
        {
            options.UIPath = "/health-ui";
            options.ApiPath = "/health-api";
        });
    }

    private static IServiceCollection ConfigureHealthCheckEndpoints(this IServiceCollection services)
    {
        services.Configure<AbpEndpointRouterOptions>(options =>
        {
            options.EndpointConfigureActions.Add(endpointContext =>
            {
                endpointContext.Endpoints.MapSwapMidHealthCheckEndpoints();
            });
        });

        return services;
    }

    /// <summary>
    /// 映射存活（/health/live）、流量就绪（/health/ready）与全依赖就绪（/health-status）三类探针。
    /// </summary>
    internal static IEndpointRouteBuilder MapSwapMidHealthCheckEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(
            new PathString(LivenessPath),
            new HealthCheckOptions
            {
                Predicate = static _ => false,
                ResponseWriter = static (context, _) =>
                {
                    context.Response.ContentLength = 0;
                    return Task.CompletedTask;
                },
                AllowCachingResponses = false,
            });

        endpoints.MapHealthChecks(
            new PathString(TrafficReadyPath),
            new HealthCheckOptions
            {
                Predicate = static registration =>
                    registration.Tags.Contains("database") || registration.Tags.Contains("cache"),
                ResponseWriter = ReadinessResponseWriter.WriteAsync,
                AllowCachingResponses = false,
            });

        endpoints.MapHealthChecks(
            new PathString(ReadinessPath),
            new HealthCheckOptions
            {
                Predicate = static _ => true,
                ResponseWriter = ReadinessResponseWriter.WriteAsync,
                AllowCachingResponses = false,
            });

        return endpoints;
    }

    private static IServiceCollection MapHealthChecksUiEndpoints(
        this IServiceCollection services,
        Action<global::HealthChecks.UI.Configuration.Options>? setupOption = null)
    {
        services.Configure<AbpEndpointRouterOptions>(routerOptions =>
        {
            routerOptions.EndpointConfigureActions.Add(endpointContext =>
            {
                endpointContext.Endpoints.MapHealthChecksUI(setupOption);
            });
        });

        return services;
    }
}
