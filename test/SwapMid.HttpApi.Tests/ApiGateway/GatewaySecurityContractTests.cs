using System.Net;
using System.Reflection;
using System.Threading;
using SwapMid.ApiGateway;
using SwapMid.ApiManagement;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using Shouldly;

namespace SwapMid.HttpApi.Tests.ApiGateway;

public class GatewaySecurityContractTests
{
    private const string GatewayPermission =
        SwapMidPermissions.ApiManagement.ApiRoutes.Default;

    [Fact]
    public void Gateway_controller_requires_the_exact_route_read_permission()
    {
        var controllerType = typeof(GatewayController);

        controllerType.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
            .ShouldBeEmpty();
        controllerType.GetMethod(nameof(GatewayController.ForwardAsync))!
            .GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
            .ShouldBeEmpty();

        var authorize = controllerType
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .ShouldHaveSingleItem();
        authorize.Policy.ShouldBe(GatewayPermission);
    }

    [Fact]
    public async Task Gateway_rejects_anonymous_and_unpermitted_requests_before_route_lookup()
    {
        using var host = CreateHost();
        using var anonymousClient = host.CreateClient(authenticated: false);
        using var unpermittedClient = host.CreateClient(authenticated: true);

        var anonymousResponse = await anonymousClient.GetAsync("/api/gateway/orders");
        var unpermittedResponse = await unpermittedClient.GetAsync("/api/gateway/orders");

        anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unpermittedResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await AssertForwardingInfrastructureWasNotUsedAsync(host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Gateway_returns_not_found_without_route_lookup_when_switch_is_not_true(
        bool? enableApiGateway)
    {
        using var host = CreateHost(enableApiGateway);
        using var client = host.CreateClient(authenticated: true, GatewayPermission);

        var response = await client.GetAsync("/api/gateway/orders");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
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
