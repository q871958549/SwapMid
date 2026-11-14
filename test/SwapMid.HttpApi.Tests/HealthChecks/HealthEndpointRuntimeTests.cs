using System.Net;
using System.Threading;
using SwapMid.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;

namespace SwapMid.HttpApi.Tests.HealthChecks;

/// <summary>
/// 执行真实 ASP.NET Core HealthCheck middleware，守住 liveness 不探依赖、
/// readiness 仍对依赖故障返回 503 的分工。
/// </summary>
public sealed class HealthEndpointRuntimeTests
{
    [Fact]
    public async Task Dependency_unhealthy_should_not_make_liveness_unhealthy()
    {
        var readinessProbeCalls = 0;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddHealthChecks()
            .AddCheck(
                "Forced dependency failure",
                () =>
                {
                    Interlocked.Increment(ref readinessProbeCalls);
                    return HealthCheckResult.Unhealthy("dependency unavailable");
                });

        await using var app = builder.Build();
        app.MapSwapMidHealthCheckEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();

        var readiness = await client.GetAsync("/health-status");
        readiness.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        readinessProbeCalls.ShouldBe(1);

        var liveness = await client.GetAsync("/health/live");
        liveness.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await liveness.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
        readinessProbeCalls.ShouldBe(
            1,
            "liveness 必须跳过所有注册探针，不能因依赖故障或慢探针误摘 Host");

        var trafficReady = await client.GetAsync("/health/ready");
        trafficReady.StatusCode.ShouldBe(HttpStatusCode.OK);
        readinessProbeCalls.ShouldBe(
            1,
            "未标记 database/cache 的探针不能决定流量就绪");
    }

    [Fact]
    public async Task Traffic_ready_runs_database_and_cache_only()
    {
        var databaseCalls = 0;
        var cacheCalls = 0;
        var queueCalls = 0;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddHealthChecks()
            .AddCheck(
                "database",
                () =>
                {
                    Interlocked.Increment(ref databaseCalls);
                    return HealthCheckResult.Unhealthy("database unavailable");
                },
                tags: ["database"])
            .AddCheck(
                "cache",
                () =>
                {
                    Interlocked.Increment(ref cacheCalls);
                    return HealthCheckResult.Healthy("cache ok");
                },
                tags: ["cache"])
            .AddCheck(
                "queue",
                () =>
                {
                    Interlocked.Increment(ref queueCalls);
                    return HealthCheckResult.Unhealthy("queue unavailable");
                },
                tags: ["mq"]);

        await using var app = builder.Build();
        app.MapSwapMidHealthCheckEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();

        var trafficReady = await client.GetAsync("/health/ready");
        trafficReady.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        databaseCalls.ShouldBe(1);
        cacheCalls.ShouldBe(1);
        queueCalls.ShouldBe(0);

        var liveness = await client.GetAsync("/health/live");
        liveness.StatusCode.ShouldBe(HttpStatusCode.OK);
        databaseCalls.ShouldBe(1, "liveness 不能因为流量就绪失败而再跑依赖探针");

        var fullReadiness = await client.GetAsync("/health-status");
        fullReadiness.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        queueCalls.ShouldBe(1, "全依赖就绪仍要覆盖消息队列等非流量探针");
    }

    [Theory]
    [InlineData("8.8.8.8", false)]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("10.168.1.20", true)]
    public async Task Readiness_details_are_limited_to_private_clients(string remoteIp, bool includeDetails)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddHealthChecks()
            .AddCheck("Forced dependency failure", () => HealthCheckResult.Unhealthy("dependency unavailable"));

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (IPAddress.TryParse(context.Request.Headers["X-Test-Remote-Ip"], out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            await next();
        });
        app.MapSwapMidHealthCheckEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health-status");
        request.Headers.TryAddWithoutValidation("X-Test-Remote-Ip", remoteIp);

        var readiness = await client.SendAsync(request);
        var body = await readiness.Content.ReadAsStringAsync();

        readiness.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        body.ShouldContain("\"status\":\"Unhealthy\"");
        if (includeDetails)
        {
            body.ShouldContain("dependency unavailable");
        }
        else
        {
            body.ShouldNotContain("dependency unavailable");
            body.ShouldNotContain("Forced dependency failure");
        }
    }
}
