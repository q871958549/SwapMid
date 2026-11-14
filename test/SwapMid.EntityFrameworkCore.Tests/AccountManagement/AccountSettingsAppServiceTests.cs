using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace SwapMid.AccountManagement;

[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
public class AccountSettingsAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IAccountSettingsAppService _settingsAppService;

    public AccountSettingsAppServiceTests()
    {
        _settingsAppService = GetRequiredService<IAccountSettingsAppService>();
    }

    [Fact]
    public async Task UpdateSecurityAsync_Should_Echo_Input_Not_Cached_Old_Value()
    {
        var result = await _settingsAppService.UpdateSecurityAsync(new UpdateAccountSecuritySettingsDto
        {
            DefaultPassword = "SwapMid@12345",
            ForceChangePasswordOnFirstLogin = false,
            PasswordRequiredLength = 8,
            PasswordRequireDigit = true,
            PasswordRequireLowercase = true,
            PasswordRequireUppercase = true,
            PasswordRequireNonAlphanumeric = true,
        });

        result.PasswordRequiredLength.ShouldBe(8);
        result.ForceChangePasswordOnFirstLogin.ShouldBeFalse();
        result.DefaultPassword.ShouldBe("SwapMid@12345");
    }

    [Fact]
    public async Task GetSecurityAsync_Should_Read_Persisted_Rules_In_New_Scope()
    {
        await _settingsAppService.UpdateSecurityAsync(new UpdateAccountSecuritySettingsDto
        {
            DefaultPassword = "SwapMid@12345678",
            ForceChangePasswordOnFirstLogin = true,
            PasswordRequiredLength = 10,
            PasswordRequireDigit = true,
            PasswordRequireLowercase = false,
            PasswordRequireUppercase = true,
            PasswordRequireNonAlphanumeric = false,
        });

        using var scope = ServiceProvider.CreateScope();
        var fresh = scope.ServiceProvider.GetRequiredService<IAccountSettingsAppService>();
        var security = await fresh.GetSecurityAsync();

        security.PasswordRequiredLength.ShouldBe(10);
        security.PasswordRequireDigit.ShouldBeTrue();
        security.PasswordRequireLowercase.ShouldBeFalse();
        security.PasswordRequireUppercase.ShouldBeTrue();
        security.PasswordRequireNonAlphanumeric.ShouldBeFalse();
    }
}
