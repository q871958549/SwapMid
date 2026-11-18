using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using SwapMid.AccountManagement;

namespace SwapMid.HttpApi.Tests.AccountManagement;

public class AccountPasswordPolicyHttpContractTests
{
    [Fact]
    public async Task Password_policy_endpoint_is_anonymous_and_returns_complexity_rules_without_default_password()
    {
        var settingsService = Substitute.For<IAccountSettingsAppService>();
        settingsService.GetPasswordPolicyAsync().Returns(new AccountPasswordPolicyDto
        {
            RequiredLength = 8,
            RequireDigit = true,
            RequireLowercase = true,
            RequireUppercase = true,
            RequireNonAlphanumeric = false,
        });

        using var host = new ContractTestHost(
            configureServices: services => services.AddSingleton(settingsService));
        using var anonymous = host.CreateClient(authenticated: false);

        var res = await anonymous.GetAsync("/api/v1/account-management/password-policy");
        res.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("requiredLength").GetInt32().ShouldBe(8);
        doc.RootElement.GetProperty("requireDigit").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("requireLowercase").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("requireUppercase").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("requireNonAlphanumeric").GetBoolean().ShouldBeFalse();
        doc.RootElement.TryGetProperty("defaultPassword", out _).ShouldBeFalse();
    }
}
