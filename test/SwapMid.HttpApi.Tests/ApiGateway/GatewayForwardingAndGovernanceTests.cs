using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using SwapMid.ApiGateway;
using SwapMid.ApiManagement;
using SwapMid.Infrastructure;
using SwapMid.Permissions;
using SwapMid.SharedKernel.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace SwapMid.HttpApi.Tests.ApiGateway;

public class GatewayForwardingAndGovernanceTests
{
    private const string GatewayPermission = SwapMidPermissions.ApiManagement.ApiRoutes.Default;

    [Theory]
    [InlineData("http://127.0.0.1:8080/internal", "127.0.0.1")]
    [InlineData("http://169.254.169.254/latest/meta-data/", "169.254.169.254")]
    [InlineData("https://untrusted.partner.invalid/v1/orders", "trusted.partner.invalid")]
    [InlineData("ftp://trusted.partner.invalid/files/report.csv", "trusted.partner.invalid")]
    public async Task Gateway_blocks_ssrf_loopback_metadata_and_unallowed_hosts_before_creating_http_client(
        string upstreamUrl,
        string allowedHosts)
    {
        var groupId = Guid.NewGuid();
        var route = new ApiRoute(
            Guid.NewGuid(),
            "Orders",
            "/v1/orders",
            HttpMethodType.GET,
            upstreamUrl,
            groupId);

        using var host = new ContractTestHost(
            enableApiGateway: true,
            extraSettings: new Dictionary<string, string?>
            {
                ["ApiGateway:AllowedHosts"] = allowedHosts,
            });
        host.ApiRouteRepository
            .FindEnabledMatchAsync("/v1/orders", HttpMethodType.GET, Arg.Any<CancellationToken>())
            .Returns(route);

        using var client = host.CreateClient(authenticated: true, GatewayPermission);
        var response = await client.GetAsync("/api/gateway/v1/orders");

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        host.HttpClientFactory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task Gateway_enforces_route_group_acl_rate_limits_and_strips_inbound_credentials()
    {
        var allowedGroupId = Guid.NewGuid();
        var forbiddenGroupId = Guid.NewGuid();
        var allowedRoute = new ApiRoute(
            Guid.NewGuid(),
            "Allowed Orders",
            "/v1/orders",
            HttpMethodType.GET,
            "https://api.partner.example/orders",
            allowedGroupId);
        var forbiddenRoute = new ApiRoute(
            Guid.NewGuid(),
            "Forbidden Billing",
            "/v1/billing",
            HttpMethodType.GET,
            "https://api.partner.example/billing",
            forbiddenGroupId);

        var governanceReader = Substitute.For<IGatewayClientGovernanceReader>();
        governanceReader.FindPolicyAsync("app-partner-limited", Arg.Any<CancellationToken>())
            .Returns(new GatewayClientPolicySnapshot(
                "app-partner-limited",
                new HashSet<Guid> { allowedGroupId },
                RateLimitPerMinute: 2));

        HttpRequestMessage? capturedUpstreamRequest = null;
        var fakeHandler = new RecordingUpstreamHandler(req =>
        {
            capturedUpstreamRequest = CloneHeaderSnapshot(req);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"upstream\":\"ok\"}", Encoding.UTF8, "application/json"),
            };
        });

        using var host = new ContractTestHost(
            enableApiGateway: true,
            configureServices: services => services.AddSingleton(governanceReader),
            extraSettings: new Dictionary<string, string?>
            {
                ["ApiGateway:AllowedHosts"] = "api.partner.example",
            });
        host.ApiRouteRepository
            .FindEnabledMatchAsync("/v1/orders", HttpMethodType.GET, Arg.Any<CancellationToken>())
            .Returns(allowedRoute);
        host.ApiRouteRepository
            .FindEnabledMatchAsync("/v1/billing", HttpMethodType.GET, Arg.Any<CancellationToken>())
            .Returns(forbiddenRoute);
        host.HttpClientFactory
            .CreateClient(ExternalHttpClientNames.ApiGateway)
            .Returns(_ => new HttpClient(fakeHandler, disposeHandler: false));

        using var partnerClient = host.CreateExternalClient("app-partner-limited");
        partnerClient.DefaultRequestHeaders.Add("Authorization", "Bearer top-secret-swapmid-token");
        partnerClient.DefaultRequestHeaders.Add("Cookie", "bff_session=super-secret-cookie");

        // 1st call: hits forbidden route group -> 403 Forbidden (consumes 1 rate-limit token)
        var aclDenied = await partnerClient.GetAsync("/api/gateway/v1/billing");
        aclDenied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var aclJson = await aclDenied.Content.ReadAsStringAsync();
        aclJson.ShouldContain("SwapMid:GW:GroupAccessDenied");

        // 2nd call: hits allowed route group -> 200 OK & credentials stripped
        var okResponse = await partnerClient.GetAsync("/api/gateway/v1/orders?page=1");
        okResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await okResponse.Content.ReadAsStringAsync()).ShouldContain("\"upstream\":\"ok\"");
        capturedUpstreamRequest.ShouldNotBeNull();
        capturedUpstreamRequest!.RequestUri!.ToString().ShouldBe("https://api.partner.example/orders?page=1");
        capturedUpstreamRequest.Headers.Contains("Authorization").ShouldBeFalse();
        capturedUpstreamRequest.Headers.Contains("Cookie").ShouldBeFalse();
        capturedUpstreamRequest.Headers.GetValues("X-SwapMid-Client-Id").ShouldContain("app-partner-limited");

        // 3rd call within same minute: exceeds RateLimitPerMinute=2 -> 429 Too Many Requests + Retry-After
        var rateLimited = await partnerClient.GetAsync("/api/gateway/v1/orders");
        rateLimited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task Gateway_events_endpoint_publishes_to_message_bus_with_deterministic_idempotency_key()
    {
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        IntegrationEventEnvelope? capturedFirst = null;
        IntegrationEventEnvelope? capturedSecond = null;
        publisher.PublishIfTopicActiveAsync(Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var env = call.Arg<IntegrationEventEnvelope>();
                if (capturedFirst is null)
                {
                    capturedFirst = env;
                }
                else
                {
                    capturedSecond = env;
                }
                return env.TopicName == "integration.pipeline.events";
            });

        using var host = new ContractTestHost(
            enableApiGateway: true,
            configureServices: services => services.AddSingleton(publisher));
        using var partnerClient = host.CreateExternalClient("app-webhook-source");

        using var firstReq = new HttpRequestMessage(HttpMethod.Post, "/api/gateway/events/integration.pipeline.events")
        {
            Content = new StringContent("{\"orderNo\":\"SO-1001\",\"status\":\"paid\"}", Encoding.UTF8, "application/json"),
        };
        firstReq.Headers.Add("Idempotency-Key", "ord-so-1001-v1");
        firstReq.Headers.Add("X-SwapMid-Subject", "orders.paid");

        var firstRes = await partnerClient.SendAsync(firstReq);
        firstRes.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var secondReq = new HttpRequestMessage(HttpMethod.Post, "/api/gateway/events/integration.pipeline.events")
        {
            Content = new StringContent("{\"orderNo\":\"SO-1001\",\"status\":\"paid\"}", Encoding.UTF8, "application/json"),
        };
        secondReq.Headers.Add("Idempotency-Key", "ord-so-1001-v1");
        var secondRes = await partnerClient.SendAsync(secondReq);
        secondRes.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        capturedFirst.ShouldNotBeNull();
        capturedSecond.ShouldNotBeNull();
        capturedFirst!.EventId.ShouldBe(capturedSecond!.EventId);
        capturedFirst.Subject.ShouldBe("orders.paid");

        var missingTopicRes = await partnerClient.PostAsJsonAsync(
            "/api/gateway/events/non.existent.topic",
            new { ping = true });
        missingTopicRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var malformedRes = await partnerClient.PostAsync(
            "/api/gateway/events/integration.pipeline.events",
            new StringContent("{not-json", Encoding.UTF8, "application/json"));
        malformedRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static HttpRequestMessage CloneHeaderSnapshot(HttpRequestMessage source)
    {
        var copy = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return copy;
    }

    private sealed class RecordingUpstreamHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
