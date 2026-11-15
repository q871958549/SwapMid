using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using NSubstitute;
using Shouldly;

namespace SwapMid.HttpApi.Tests.AccountManagement;

public class ExternalOidcContractTests
{
    [Fact]
    public async Task Auth_providers_endpoint_is_anonymous_and_disabled_by_default()
    {
        using var host = new ContractTestHost();
        using var anonymous = host.CreateClient(authenticated: false);

        var res = await anonymous.GetAsync("/api/v1/auth-providers");
        res.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("externalOidcEnabled").GetBoolean().ShouldBeFalse();
        doc.RootElement.TryGetProperty("clientSecret", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Auth_providers_endpoint_exposes_public_oidc_metadata_without_leaking_client_secret()
    {
        using var host = new ContractTestHost(
            extraSettings: new Dictionary<string, string?>
            {
                ["AuthServer:ExternalOidc:Enabled"] = "true",
                ["AuthServer:ExternalOidc:DisplayName"] = "Keycloak Enterprise SSO",
                ["AuthServer:ExternalOidc:Authority"] = "https://sso.partner.example/realms/main/",
                ["AuthServer:ExternalOidc:ClientId"] = "swapmid-web-sso",
                ["AuthServer:ExternalOidc:ClientSecret"] = "SUPER-SECRET-OIDC-KEY",
            });
        host.HttpClientFactory
            .CreateClient("ExternalOidc")
            .Returns(_ => new HttpClient(new StubDiscoveryHandler("https://sso.partner.example/realms/main/protocol/openid-connect/auth")));

        using var anonymous = host.CreateClient(authenticated: false);

        var res = await anonymous.GetAsync("/api/v1/auth-providers");
        res.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await res.Content.ReadAsStringAsync();
        body.ShouldNotContain("SUPER-SECRET-OIDC-KEY");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("externalOidcEnabled").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("displayName").GetString().ShouldBe("Keycloak Enterprise SSO");
        doc.RootElement.GetProperty("authority").GetString().ShouldBe("https://sso.partner.example/realms/main");
        doc.RootElement.GetProperty("clientId").GetString().ShouldBe("swapmid-web-sso");
        doc.RootElement.GetProperty("authorizationEndpoint").GetString()
            .ShouldBe("https://sso.partner.example/realms/main/protocol/openid-connect/auth");
    }

    private sealed class StubDiscoveryHandler(string authorizationEndpoint) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new
            {
                issuer = "https://sso.partner.example/realms/main",
                authorization_endpoint = authorizationEndpoint,
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }
}
