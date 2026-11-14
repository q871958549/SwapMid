using Localization.Resources.AbpUi;
using SwapMid.HttpApi.Versioning;
using SwapMid.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Account;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.HttpApi;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;

namespace SwapMid;

[DependsOn(
    typeof(SwapMidApplicationContractsModule),
    typeof(AbpPermissionManagementHttpApiModule),
    typeof(AbpSettingManagementHttpApiModule),
    typeof(AbpAccountHttpApiModule),
    typeof(AbpIdentityHttpApiModule),
    typeof(AbpTenantManagementHttpApiModule),
    typeof(AbpFeatureManagementHttpApiModule)
)]
public class SwapMidHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ConfigureLocalization();
        ConfigureApiDeprecation(context);
    }

    private void ConfigureApiDeprecation(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<ApiDeprecationHeaderFilter>();
        Configure<MvcOptions>(options =>
        {
            options.Filters.AddService<ApiDeprecationHeaderFilter>();
        });
    }

    private void ConfigureLocalization()
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<SwapMidResource>()
                .AddBaseTypes(
                    typeof(AbpUiResource)
                );
        });
    }
}
