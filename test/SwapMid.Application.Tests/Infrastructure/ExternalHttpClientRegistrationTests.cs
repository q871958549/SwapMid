using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Shouldly;
using Xunit;

namespace SwapMid.Infrastructure;

public class ExternalHttpClientRegistrationTests
{
    public static TheoryData<string> NamedClientNames => new()
    {
        ExternalHttpClientNames.Prometheus,
        ExternalHttpClientNames.MessageDeliveryHttp,
        ExternalHttpClientNames.MessageDeliveryBotWebhook,
        ExternalHttpClientNames.NotificationWebhook,
    };

    public static TheoryData<string> WebhookDeliveryClientNames => new()
    {
        ExternalHttpClientNames.MessageDeliveryHttp,
        ExternalHttpClientNames.MessageDeliveryBotWebhook,
        ExternalHttpClientNames.NotificationWebhook,
    };

    public static TheoryData<string> ThirtySecondDeliveryClientNames => new()
    {
        ExternalHttpClientNames.MessageDeliveryHttp,
        ExternalHttpClientNames.MessageDeliveryBotWebhook,
    };

    [Theory]
    [MemberData(nameof(NamedClientNames))]
    public async Task Http500不应触发隐式重试(string clientName)
    {
        var handler = new StubHttpMessageHandler
        {
            Handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
        };
        using var provider = BuildProvider(handler);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        using var response = await client.GetAsync("https://external-http.test/");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        handler.CallCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(ExternalHttpClientNames.MessageDeliveryHttp,
        "https://webhook.example.test/delivery?token=must-not-be-logged")]
    [InlineData(ExternalHttpClientNames.MessageDeliveryBotWebhook,
        "https://oapi.dingtalk.com/robot/send?access_token=must-not-be-logged")]
    [InlineData(ExternalHttpClientNames.NotificationWebhook,
        "https://hooks.slack.com/services/T00/B00/must-not-be-logged")]
    public async Task 含查询串或路径凭据的Webhook客户端必须移除默认HttpClient日志(
        string clientName,
        string requestUri)
    {
        var handler = new StubHttpMessageHandler();
        var loggerProvider = new CapturingLoggerProvider();
        using var provider = BuildProvider(handler, loggerProvider: loggerProvider);
        using var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(clientName);

        using var response = await client.GetAsync(requestUri);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        loggerProvider.Entries
            .ShouldNotContain(entry => entry.Category.StartsWith(
                $"System.Net.Http.HttpClient.{clientName}",
                StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ThirtySecondDeliveryClientNames))]
    public void 消息投递默认应保持三十秒管道与三十五秒外层兜底(string clientName)
    {
        var handler = new StubHttpMessageHandler();
        using var provider = BuildProvider(handler);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        client.Timeout.ShouldBe(TimeSpan.FromSeconds(35));
    }

    [Theory]
    [MemberData(nameof(WebhookDeliveryClientNames))]
    public void Webhook超时应支持按客户端独立覆盖(string clientName)
    {
        var handler = new StubHttpMessageHandler();
        using var provider = BuildProvider(handler, new Dictionary<string, string?>
        {
            [$"ExternalHttp:{clientName}:TimeoutSeconds"] = "6",
        });
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        client.Timeout.ShouldBe(TimeSpan.FromSeconds(11));
    }

    [Theory]
    [MemberData(nameof(WebhookDeliveryClientNames))]
    public void Webhook生产Handler应禁用Cookie重定向代理并在建连时复核目标(string clientName)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        ExternalHttpClientRegistration.AddClients(services, configuration);
        using var provider = services.BuildServiceProvider();

        var primaryHandler = GetPrimaryHandler(provider, clientName);

        primaryHandler.ShouldBeOfType<SocketsHttpHandler>();
        var socketsHandler = (SocketsHttpHandler)primaryHandler;
        socketsHandler.UseCookies.ShouldBeFalse();
        socketsHandler.AllowAutoRedirect.ShouldBeFalse();
        socketsHandler.UseProxy.ShouldBeFalse();
        socketsHandler.ConnectCallback.ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(WebhookDeliveryClientNames))]
    public async Task Webhook连续失败不应熔断其它健康端点(string clientName)
    {
        var handler = new StubHttpMessageHandler
        {
            Handler = (request, _) => Task.FromResult(new HttpResponseMessage(
                request.RequestUri!.AbsolutePath == "/healthy"
                    ? HttpStatusCode.OK
                    : HttpStatusCode.InternalServerError)),
        };
        using var provider = BuildProvider(handler);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        for (var i = 0; i < 10; i++)
        {
            using var failed = await client.GetAsync("https://failed-endpoint.test/failure");
            failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }

        using var healthy = await client.GetAsync("https://healthy-endpoint.test/healthy");
        healthy.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.CallCount.ShouldBe(11);
    }

    [Theory]
    [MemberData(nameof(WebhookDeliveryClientNames))]
    public async Task Webhook慢端点应由管道超时且只访问一次(string clientName)
    {
        var handler = new StubHttpMessageHandler
        {
            Handler = async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
        };
        using var provider = BuildProvider(handler, new Dictionary<string, string?>
        {
            [$"ExternalHttp:{clientName}:TimeoutSeconds"] = "1",
        });
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        await Should.ThrowAsync<TimeoutRejectedException>(
            () => client.GetAsync("https://slow-endpoint.test/slow"));
        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Prometheus管道内超时应被外层熔断器计数()
    {
        var handler = new StubHttpMessageHandler
        {
            Handler = async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
        };
        using var provider = BuildProvider(handler, new Dictionary<string, string?>
        {
            ["ExternalHttp:Prometheus:TimeoutSeconds"] = "1",
            ["ExternalHttp:Prometheus:FailureRatio"] = "1",
            ["ExternalHttp:Prometheus:MinimumThroughput"] = "2",
        });
        using var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ExternalHttpClientNames.Prometheus);

        await Task.WhenAll(
            Should.ThrowAsync<TimeoutRejectedException>(
                () => client.GetAsync("https://external-http.test/timeout-1")),
            Should.ThrowAsync<TimeoutRejectedException>(
                () => client.GetAsync("https://external-http.test/timeout-2")));

        await Should.ThrowAsync<BrokenCircuitException>(
            () => client.GetAsync("https://external-http.test/open-after-timeouts"));
        handler.CallCount.ShouldBe(2);
    }

    [Fact]
    public void Prometheus旧超时配置应继续生效()
    {
        var handler = new StubHttpMessageHandler();
        using var provider = BuildProvider(handler, new Dictionary<string, string?>
        {
            ["Prometheus:QueryTimeout"] = "7",
        });
        using var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ExternalHttpClientNames.Prometheus);

        client.Timeout.ShouldBe(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void 四项熔断参数与超时应支持逐客户端覆盖()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalHttp:Prometheus:TimeoutSeconds"] = "6",
                ["ExternalHttp:Prometheus:FailureRatio"] = "0.75",
                ["ExternalHttp:Prometheus:MinimumThroughput"] = "12",
                ["ExternalHttp:Prometheus:SamplingDurationSeconds"] = "40",
                ["ExternalHttp:Prometheus:BreakDurationSeconds"] = "20",
            })
            .Build();

        var settings = ExternalHttpClientRegistration.GetSettings(
            configuration,
            ExternalHttpClientNames.Prometheus,
            null);

        settings.Timeout.ShouldBe(TimeSpan.FromSeconds(6));
        settings.FailureRatio.ShouldBe(0.75);
        settings.MinimumThroughput.ShouldBe(12);
        settings.SamplingDuration.ShouldBe(TimeSpan.FromSeconds(40));
        settings.BreakDuration.ShouldBe(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task 熔断期满后半开探测成功应闭合_FakeTimeProvider无需真实等待()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var fakeTime = new FakeTimeProvider();
        var settings = new ExternalHttpResilienceSettings(
            Timeout: TimeSpan.FromSeconds(10),
            FailureRatio: 1,
            MinimumThroughput: 2,
            SamplingDuration: TimeSpan.FromSeconds(30),
            BreakDuration: TimeSpan.FromSeconds(15));
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>
        {
            TimeProvider = fakeTime,
        };
        ExternalHttpClientRegistration.AddStrategies(builder, settings);
        var pipeline = builder.Build();

        for (var i = 0; i < 2; i++)
        {
            using var failed = await pipeline.ExecuteAsync(
                static _ => new ValueTask<HttpResponseMessage>(
                    new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        }

        var probeCalls = 0;
        await Should.ThrowAsync<BrokenCircuitException>(async () =>
        {
            using var _ = await pipeline.ExecuteAsync(_ =>
            {
                probeCalls++;
                return new ValueTask<HttpResponseMessage>(new HttpResponseMessage(HttpStatusCode.OK));
            });
        });
        probeCalls.ShouldBe(0);

        fakeTime.Advance(settings.BreakDuration + TimeSpan.FromMilliseconds(1));

        using var probe = await pipeline.ExecuteAsync(_ =>
        {
            probeCalls++;
            return new ValueTask<HttpResponseMessage>(new HttpResponseMessage(HttpStatusCode.OK));
        });
        probe.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var afterClosed = await pipeline.ExecuteAsync(_ =>
        {
            probeCalls++;
            return new ValueTask<HttpResponseMessage>(new HttpResponseMessage(HttpStatusCode.OK));
        });
        afterClosed.StatusCode.ShouldBe(HttpStatusCode.OK);
        probeCalls.ShouldBe(2);
    }

    private static ServiceProvider BuildProvider(
        StubHttpMessageHandler handler,
        IReadOnlyDictionary<string, string?>? values = null,
        ILoggerProvider? loggerProvider = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
        var services = new ServiceCollection();
        if (loggerProvider != null)
        {
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddProvider(loggerProvider);
            });
        }

        ExternalHttpClientRegistration.AddClients(services, configuration);
        foreach (var clientName in NamedClientNames)
        {
            services.AddHttpClient(clientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        }

        return services.BuildServiceProvider();
    }

    private static HttpMessageHandler GetPrimaryHandler(ServiceProvider provider, string clientName)
    {
        var handlerFactory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        HttpMessageHandler current = handlerFactory.CreateHandler(clientName);
        while (current is DelegatingHandler delegatingHandler)
        {
            current = delegatingHandler.InnerHandler
                ?? throw new InvalidOperationException($"{clientName} HttpMessageHandler 管道存在空 InnerHandler");
        }

        return current;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(
            string category,
            ConcurrentQueue<(string Category, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                entries.Enqueue((category, formatter(state, exception)));
            }
        }
    }
}
