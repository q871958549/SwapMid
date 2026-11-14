using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace SwapMid.Infrastructure;

/// <summary>
/// 外部 HTTP 命名客户端集中注册（Polly 熔断/超时 + SSRF / DNS Rebinding 建连守卫）。
/// </summary>
internal static class ExternalHttpClientRegistration
{
    private const int DefaultTimeoutSeconds = 10;
    private const int DefaultMessageDeliveryTimeoutSeconds = 30;

    internal static void AddClients(IServiceCollection services, IConfiguration configuration)
    {
        AddResilientClient(
            services,
            configuration,
            ExternalHttpClientNames.Prometheus,
            legacyTimeoutKey: "Prometheus:QueryTimeout");

        AddTimeoutOnlyClient(
            services,
            configuration,
            ExternalHttpClientNames.MessageDeliveryHttp,
            DefaultMessageDeliveryTimeoutSeconds,
            ExternalHttpConfigurationKeys.MessageDeliveryAllowedHosts,
            ExternalHttpConfigurationKeys.MessageDeliveryAllowedPrivateNetworks);

        AddTimeoutOnlyClient(
            services,
            configuration,
            ExternalHttpClientNames.MessageDeliveryBotWebhook,
            DefaultMessageDeliveryTimeoutSeconds,
            ExternalHttpConfigurationKeys.BotDeliveryAllowedHosts,
            ExternalHttpConfigurationKeys.BotDeliveryAllowedPrivateNetworks,
            builtInAllowedHosts: OutboundEndpointConnectionGuard.DefaultBotWebhookHosts);

        AddTimeoutOnlyClient(
            services,
            configuration,
            ExternalHttpClientNames.NotificationWebhook,
            DefaultTimeoutSeconds,
            ExternalHttpConfigurationKeys.NotificationWebhookAllowedHosts,
            ExternalHttpConfigurationKeys.NotificationWebhookAllowedPrivateNetworks,
            builtInAllowedHosts: OutboundEndpointConnectionGuard.DefaultBotWebhookHosts);
    }

    private static void AddTimeoutOnlyClient(
        IServiceCollection services,
        IConfiguration configuration,
        string clientName,
        int defaultTimeoutSeconds,
        string? allowedHostsKey = null,
        string? allowedPrivateNetworksKey = null,
        string? fixedHost = null,
        int? fixedPort = null,
        IReadOnlyCollection<string>? builtInAllowedHosts = null)
    {
        var timeout = GetTimeout(configuration, clientName, defaultTimeoutSeconds);
        var clientBuilder = services.AddHttpClient(clientName, client =>
            {
                client.Timeout = timeout + TimeSpan.FromSeconds(5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                UseProxy = false,
                ConnectCallback = (context, cancellationToken) =>
                    OutboundEndpointConnectionGuard.ConnectAsync(
                        context.DnsEndPoint,
                        configuration,
                        allowedHostsKey,
                        allowedPrivateNetworksKey,
                        fixedHost,
                        fixedPort,
                        cancellationToken,
                        builtInAllowedHosts),
            });

        clientBuilder.RemoveAllLoggers();
        clientBuilder.AddResilienceHandler($"{clientName.ToLowerInvariant()}-pipeline", builder =>
            builder.AddTimeout(timeout));
    }

    private static void AddResilientClient(
        IServiceCollection services,
        IConfiguration configuration,
        string clientName,
        string? legacyTimeoutKey = null,
        int defaultTimeoutSeconds = DefaultTimeoutSeconds,
        bool suppressDefaultRequestLogging = false)
    {
        var settings = GetSettings(configuration, clientName, legacyTimeoutKey, defaultTimeoutSeconds);

        var clientBuilder = services.AddHttpClient(clientName, client =>
            {
                client.Timeout = settings.Timeout + TimeSpan.FromSeconds(5);
            });

        if (suppressDefaultRequestLogging)
        {
            clientBuilder.RemoveAllLoggers();
        }

        clientBuilder.AddResilienceHandler($"{clientName.ToLowerInvariant()}-pipeline", builder =>
            {
                AddStrategies(builder, settings);
            });
    }

    internal static void AddStrategies(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        ExternalHttpResilienceSettings settings)
    {
        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = settings.FailureRatio,
            MinimumThroughput = settings.MinimumThroughput,
            SamplingDuration = settings.SamplingDuration,
            BreakDuration = settings.BreakDuration,
        });
        builder.AddTimeout(settings.Timeout);
    }

    internal static ExternalHttpResilienceSettings GetSettings(
        IConfiguration configuration,
        string clientName,
        string? legacyTimeoutKey,
        int defaultTimeoutSeconds = DefaultTimeoutSeconds)
    {
        var prefix = $"ExternalHttp:{clientName}";
        var timeoutSeconds = configuration.GetValue<int?>($"{prefix}:TimeoutSeconds")
            ?? (legacyTimeoutKey == null ? null : configuration.GetValue<int?>(legacyTimeoutKey))
            ?? defaultTimeoutSeconds;

        return new ExternalHttpResilienceSettings(
            Timeout: TimeSpan.FromSeconds(timeoutSeconds),
            FailureRatio: configuration.GetValue<double?>($"{prefix}:FailureRatio") ?? 0.5,
            MinimumThroughput: configuration.GetValue<int?>($"{prefix}:MinimumThroughput") ?? 10,
            SamplingDuration: TimeSpan.FromSeconds(
                configuration.GetValue<int?>($"{prefix}:SamplingDurationSeconds") ?? 30),
            BreakDuration: TimeSpan.FromSeconds(
                configuration.GetValue<int?>($"{prefix}:BreakDurationSeconds") ?? 15));
    }

    internal static TimeSpan GetTimeout(
        IConfiguration configuration,
        string clientName,
        int defaultTimeoutSeconds)
        => TimeSpan.FromSeconds(
            configuration.GetValue<int?>($"ExternalHttp:{clientName}:TimeoutSeconds")
            ?? defaultTimeoutSeconds);
}

internal readonly record struct ExternalHttpResilienceSettings(
    TimeSpan Timeout,
    double FailureRatio,
    int MinimumThroughput,
    TimeSpan SamplingDuration,
    TimeSpan BreakDuration);
