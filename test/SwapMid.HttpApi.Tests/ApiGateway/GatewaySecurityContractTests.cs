using System.Net;
using System.Reflection;
using System.Threading;
using SwapMid.ApiGateway;
using SwapMid.ApiManagement;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using Shouldly;
using Volo.Abp.Uow;

namespace SwapMid.HttpApi.Tests.ApiGateway;

public class GatewaySecurityContractTests
{
    private const string GatewayPermission =
        SwapMidPermissions.ApiManagement.ApiRoutes.Default;

    [Fact]
    public void Gateway_controllers_require_the_governed_gateway_access_policy_and_disable_ambient_uow()
    {
        foreach (var controllerType in new[] { typeof(GatewayController), typeof(GatewayEventsController) })
        {
            controllerType.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
                .ShouldBeEmpty();

            var authorize = controllerType
                .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .ShouldHaveSingleItem();
            authorize.Policy.ShouldBe(GatewayAuthorizationPolicies.GatewayAccessPolicy);
        }

        var forwardMethod = typeof(GatewayController).GetMethod(nameof(GatewayController.ForwardAsync))!;
        forwardMethod.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).ShouldBeEmpty();
        forwardMethod.GetCustomAttribute<UnitOfWorkAttribute>()?.IsDisabled.ShouldBeTrue();

        var ingestMethod = typeof(GatewayEventsController).GetMethod(nameof(GatewayEventsController.IngestAsync))!;
        ingestMethod.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).ShouldBeEmpty();
        ingestMethod.GetCustomAttribute<UnitOfWorkAttribute>()?.IsDisabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Gateway_rejects_anonymous_unpermitted_and_clientless_scope_requests_before_route_lookup()
    {
        using var host = CreateHost();
        using var anonymousClient = host.CreateClient(authenticated: false);
        using var unpermittedClient = host.CreateClient(authenticated: true);
        using var scopeWithoutClientId = host.CreateClientWithScopes(authenticated: true, ["external-integration"]);

        var anonymousResponse = await anonymousClient.GetAsync("/api/gateway/orders");
        var unpermittedResponse = await unpermittedClient.GetAsync("/api/gateway/orders");
        var clientlessScopeResponse = await scopeWithoutClientId.GetAsync("/api/gateway/orders");

        anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unpermittedResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        clientlessScopeResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await AssertForwardingInfrastructureWasNotUsedAsync(host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Gateway_returns_not_found_without_route_lookup_when_switch_is_not_true(
        bool? enableApiGateway)
    {
        using var host = CreateHost(enableApiGateway);
        using var operatorClient = host.CreateClient(authenticated: true, GatewayPermission);
        using var externalClient = host.CreateExternalClient("app-partner-01");

        var operatorResponse = await operatorClient.GetAsync("/api/gateway/orders");
        var externalResponse = await externalClient.GetAsync("/api/gateway/orders");
        var eventsResponse = await externalClient.PostAsync(
            "/api/gateway/events/integration.pipeline.events",
            new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json"));

        operatorResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        externalResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        eventsResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await AssertForwardingInfrastructureWasNotUsedAsync(host);
    }

    private static ContractTestHost CreateHost(bool? enableApiGateway = null)
    {
        return new ContractTestHost(
            enableApiGateway: enableApiGateway);
    }

    private static async Task AssertForwardingInfrastructureWasNotUsedAsync(ContractTestHost host)
    {
        await host.ApiRouteRepository.DidNotReceive().FindEnabledMatchAsync(
            Arg.Any<string>(),
            Arg.Any<HttpMethodType>(),
            Arg.Any<CancellationToken>());
        host.HttpClientFactory.DidNotReceive().CreateClient(Arg.Any<string>());
    }
}
