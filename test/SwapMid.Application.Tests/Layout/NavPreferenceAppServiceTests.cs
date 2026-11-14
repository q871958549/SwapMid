using System.Threading.Tasks;
using SwapMid.Settings;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.DependencyInjection;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.Users;
using Xunit;

namespace SwapMid.Layout;

public class NavPreferenceAppServiceTests
{
    private const string NavOrder = SwapMidSettings.LayoutNavOrder;

    private readonly ISettingManager _settings = Substitute.For<ISettingManager>();

    private NavPreferenceAppService CreateService(bool isAdmin)
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsInRole("admin").Returns(isAdmin);
        var service = new NavPreferenceAppService(Substitute.For<ISettingProvider>(), _settings)
        {
            LazyServiceProvider = new AbpLazyServiceProvider(
                new ServiceCollection().AddSingleton(user).BuildServiceProvider()),
        };
        return service;
    }

    [Fact]
    public async Task Non_Admin_Save_Writes_Personal_Not_Global()
    {
        await CreateService(isAdmin: false).SaveAsync(new SaveNavOrderInput { Value = "{\"groupOrder\":[\"a\"]}" });

        await _settings.Received(1).SetAsync(
            NavOrder,
            "{\"groupOrder\":[\"a\"]}",
            UserSettingValueProvider.ProviderName,
            null,
            false);
        await _settings.DidNotReceive().SetAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            GlobalSettingValueProvider.ProviderName,
            Arg.Any<string?>(),
            Arg.Any<bool>());
    }

    [Fact]
    public async Task Admin_Save_And_Reset_Write_Global_Default()
    {
        var service = CreateService(isAdmin: true);

        await service.SaveAsync(new SaveNavOrderInput { Value = "{\"groupOrder\":[\"b\"]}" });
        await service.ResetAsync();

        await _settings.Received(1).SetGlobalAsync(NavOrder, "{\"groupOrder\":[\"b\"]}");
        await _settings.Received(1).SetGlobalAsync(NavOrder, null!);
    }

    [Fact]
    public async Task Get_Returns_Stored_Order_And_Admin_Flag()
    {
        var provider = Substitute.For<ISettingProvider>();
        provider.GetOrNullAsync(NavOrder).Returns("{\"groupOrder\":[\"integration\"]}");
        var user = Substitute.For<ICurrentUser>();
        user.IsInRole("admin").Returns(true);
        var service = new NavPreferenceAppService(provider, _settings)
        {
            LazyServiceProvider = new AbpLazyServiceProvider(
                new ServiceCollection().AddSingleton(user).BuildServiceProvider()),
        };

        var dto = await service.GetAsync();
        dto.Value.ShouldBe("{\"groupOrder\":[\"integration\"]}");
        dto.IsAdmin.ShouldBeTrue();
    }
}
