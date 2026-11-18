using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using SwapMid.Settings;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.Users;
using Xunit;

namespace SwapMid.Layout;

public class TablePreferenceAppServiceTests
{
    private static TablePreferenceAppService CreateService(
        ISettingProvider? provider = null,
        ISettingManager? manager = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.Id.Returns(Guid.NewGuid());
        return new TablePreferenceAppService(
            provider ?? Substitute.For<ISettingProvider>(),
            manager ?? Substitute.For<ISettingManager>())
        {
            LazyServiceProvider = new AbpLazyServiceProvider(
                new ServiceCollection().AddSingleton(user).BuildServiceProvider()),
        };
    }

    [Fact]
    public async Task GetAsync_Normalizes_Density_And_Parses_Stored_Views()
    {
        var provider = Substitute.For<ISettingProvider>();
        provider.GetOrNullAsync(SwapMidSettings.PreferenceTableDensity).Returns("compact");
        provider.GetOrNullAsync(SwapMidSettings.PreferenceTableViews)
            .Returns("""{"cdc-streams":{"pageSize":50,"visibility":{"slotName":false}}}""");

        var snapshot = await CreateService(provider: provider).GetAsync();

        snapshot.Density.ShouldBe("compact");
        snapshot.Tables.ShouldContainKey("cdc-streams");
        snapshot.Tables["cdc-streams"].PageSize.ShouldBe(50);
        snapshot.Tables["cdc-streams"].Visibility!["slotName"].ShouldBeFalse();
    }

    [Fact]
    public async Task SaveDensityAsync_And_SaveTableViewAsync_Persist_Per_User_And_Guard_Length()
    {
        var provider = Substitute.For<ISettingProvider>();
        var manager = Substitute.For<ISettingManager>();
        var service = CreateService(provider, manager);

        await service.SaveDensityAsync(new SaveTableDensityInput { Density = "COMPACT" });
        await manager.Received(1).SetAsync(
            SwapMidSettings.PreferenceTableDensity,
            "compact",
            UserSettingValueProvider.ProviderName,
            Arg.Any<string?>(),
            false);

        await service.SaveTableViewAsync(new SaveTableViewPreferenceInput
        {
            TableKey = "orders",
            View = new TableViewPreferenceDto { PageSize = 20 },
        });
        await manager.Received(1).SetAsync(
            SwapMidSettings.PreferenceTableViews,
            Arg.Is<string>(s => s.Contains("\"orders\"") && s.Contains("\"pageSize\":20")),
            UserSettingValueProvider.ProviderName,
            Arg.Any<string?>(),
            false);

        var ex = await Should.ThrowAsync<BusinessException>(() => service.SaveTableViewAsync(new SaveTableViewPreferenceInput
        {
            TableKey = "oversized",
            View = new TableViewPreferenceDto { Order = [new string('x', TablePreferenceAppService.MaxSerializedViewsLength + 100)] },
        }));
        ex.Code.ShouldBe(SwapMidDomainErrorCodes.Layout.TablePreferenceTooLong);
    }
}
