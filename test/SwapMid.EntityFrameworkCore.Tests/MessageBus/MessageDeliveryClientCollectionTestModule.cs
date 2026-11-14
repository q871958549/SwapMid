using System;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SwapMid.DataIntegration;
using SwapMid.Infrastructure;
using Volo.Abp;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.Uow;

namespace SwapMid.EntityFrameworkCore;

/// <summary>
/// 与 <see cref="SwapMidEntityFrameworkCoreTestModule"/> 同款 SQLite 内存库建法，唯一差异：
/// 不剥离 <c>IMessageDeliveryClient</c> 真实实现（Http / RabbitMq / BotWebhook），专供
/// <see cref="SwapMid.MessageBus.MessageDeliveryClientCollectionTests"/> 验证约定注册完整性。
/// </summary>
[DependsOn(
    typeof(SwapMidApplicationTestModule),
    typeof(SwapMidEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreSqliteModule)
)]
public class MessageDeliveryClientCollectionTestModule : AbpModule
{
    private SqliteConnection? _sqliteConnection;

    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<AbpSqliteOptions>(x => x.BusyTimeout = null);
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<FeatureManagementOptions>(options =>
        {
            options.SaveStaticFeaturesToDatabase = false;
            options.IsDynamicFeatureStoreEnabled = false;
        });
        Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = false;
        });
        Configure<SettingManagementOptions>(options =>
        {
            options.SaveStaticSettingsToDatabase = false;
            options.IsDynamicSettingStoreEnabled = false;
        });

        context.Services.AddAlwaysDisableUnitOfWorkTransaction();
        context.Services.AddSingleton<IHostEnvironment, FakeHostEnvironment>();

        ConfigureInMemorySqlite(context.Services);

        context.Services.Replace(ServiceDescriptor.Singleton<IEtlPipelineRunner, FakeEtlPipelineRunner>());
    }

    private void ConfigureInMemorySqlite(IServiceCollection services)
    {
        _sqliteConnection = SqliteTestDatabaseTemplates.CloneSchemaOnly();

        services.Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(ctx => ctx.DbContextOptions.UseSqlite(_sqliteConnection));
            options.Configure<IdentityDbContext>(ctx => ctx.DbContextOptions.UseSqlite(_sqliteConnection));
            options.Configure<PermissionManagementDbContext>(ctx => ctx.DbContextOptions.UseSqlite(_sqliteConnection));
            options.Configure<SettingManagementDbContext>(ctx => ctx.DbContextOptions.UseSqlite(_sqliteConnection));
            options.Configure<OpenIddictDbContext>(ctx => ctx.DbContextOptions.UseSqlite(_sqliteConnection));
            options.Configure<BackgroundJobsDbContext>(ctx => ctx.DbContextOptions.UseSqlite(_sqliteConnection));
        });

        services.Configure<AbpUnitOfWorkDefaultOptions>(options =>
        {
            options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled;
        });
    }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        try
        {
            _sqliteConnection?.Dispose();
        }
        catch (NullReferenceException)
        {
        }
    }
}
