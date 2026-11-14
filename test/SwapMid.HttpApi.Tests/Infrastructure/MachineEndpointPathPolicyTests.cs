using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Shouldly;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;

namespace SwapMid.HttpApi.Tests.Infrastructure;

public sealed class MachineEndpointPathPolicyTests
{
    [Theory]
    [InlineData("/api")]
    [InlineData("/api/v1/data-integration/pipelines")]
    [InlineData("/API/v1/message-bus/topics")]
    [InlineData("/gateway")]
    [InlineData("/gateway/orders/sync")]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/health-status")]
    [InlineData("/metrics")]
    public void Declared_machine_endpoint_prefixes_should_be_recognized(string path)
    {
        MachineEndpointPathPolicy.IsMachineEndpoint(path).ShouldBeTrue();
        MachineEndpointPathPolicy.ShouldUseHtmlErrorPage(path).ShouldBeFalse();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/swagger")]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/hangfire")]
    [InlineData("/Account/Login")]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/authorize/callback")]
    [InlineData("/connect/endsession")]
    [InlineData("/apiary")]
    [InlineData("/connectivity")]
    [InlineData("/gateway-docs")]
    public void Browser_and_near_prefix_paths_should_keep_the_html_error_page(string path)
    {
        MachineEndpointPathPolicy.IsMachineEndpoint(path).ShouldBeFalse();
        MachineEndpointPathPolicy.ShouldUseHtmlErrorPage(path).ShouldBeTrue();
    }

    [Theory]
    [InlineData("/api/private")]
    [InlineData("/gateway/private")]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/health-status")]
    [InlineData("/metrics")]
    public async Task Machine_cookie_challenges_should_return_status_without_redirect(string path)
    {
        var login = CreateRedirectContext(path, "/Account/Login?returnUrl=%2Fapi%2Fprivate");
        await MachineEndpointPathPolicy.HandleLoginRedirectAsync(login);
        login.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        login.Response.Headers.Location.Count.ShouldBe(0);

        var accessDenied = CreateRedirectContext(path, "/Account/AccessDenied");
        await MachineEndpointPathPolicy.HandleAccessDeniedRedirectAsync(accessDenied);
        accessDenied.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        accessDenied.Response.Headers.Location.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/hangfire")]
    [InlineData("/Account/Login")]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/endsession")]
    public async Task Browser_cookie_challenges_should_keep_redirect_behavior(string path)
    {
        const string loginUri = "/Account/Login?returnUrl=%2Fswagger";
        var login = CreateRedirectContext(path, loginUri);
        await MachineEndpointPathPolicy.HandleLoginRedirectAsync(login);
        login.Response.StatusCode.ShouldBe(StatusCodes.Status302Found);
        login.Response.Headers.Location.ToString().ShouldBe(loginUri);

        const string accessDeniedUri = "/Account/AccessDenied";
        var accessDenied = CreateRedirectContext(path, accessDeniedUri);
        await MachineEndpointPathPolicy.HandleAccessDeniedRedirectAsync(accessDenied);
        accessDenied.Response.StatusCode.ShouldBe(StatusCodes.Status302Found);
        accessDenied.Response.Headers.Location.ToString().ShouldBe(accessDeniedUri);
    }

    [Theory]
    [InlineData("/health/live/missing", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/missing", HttpStatusCode.NotFound)]
    [InlineData("/gateway/unauthorized", HttpStatusCode.Unauthorized)]
    [InlineData("/api/v1/forbidden", HttpStatusCode.Forbidden)]
    public async Task Machine_status_codes_should_not_be_rewritten_by_the_production_error_page(
        string path,
        HttpStatusCode expectedStatus)
    {
        await using var app = await CreateProductionErrorPageAppAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(path);

        response.StatusCode.ShouldBe(expectedStatus);
        response.Headers.Location.ShouldBeNull();
        (response.Content.Headers.ContentType?.MediaType ?? string.Empty).ShouldNotBe("text/html");
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Browser_not_found_should_keep_the_production_error_page_redirect()
    {
        await using var app = await CreateProductionErrorPageAppAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/mvc/missing");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location!.OriginalString.ShouldBe("/Error?httpStatusCode=404");
    }

    private static RedirectContext<CookieAuthenticationOptions> CreateRedirectContext(
        string path,
        string redirectUri)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;

        return new RedirectContext<CookieAuthenticationOptions>(
            httpContext,
            new AuthenticationScheme(
                CookieAuthenticationDefaults.AuthenticationScheme,
                CookieAuthenticationDefaults.AuthenticationScheme,
                typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationProperties(),
            redirectUri);
    }

    private static async Task<WebApplication> CreateProductionErrorPageAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            Args = [],
        });
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.UseWhen(
            context => MachineEndpointPathPolicy.ShouldUseHtmlErrorPage(context.Request.Path),
            branch => branch.UseErrorPage());
        app.MapGet(
            "/gateway/unauthorized",
            () => Results.StatusCode(StatusCodes.Status401Unauthorized));
        app.MapGet(
            "/api/v1/forbidden",
            () => Results.StatusCode(StatusCodes.Status403Forbidden));

        await app.StartAsync();
        return app;
    }
}
