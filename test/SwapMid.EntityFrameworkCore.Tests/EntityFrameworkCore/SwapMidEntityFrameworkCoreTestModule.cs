using System;
using SwapMid.DataIntegration;
using SwapMid.Infrastructure;
using SwapMid.MessageBus;
using SwapMid.Notification;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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

[DependsOn(
    typeof(SwapMidApplicationTestModule),
    typeof(SwapMidEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreSqliteModule)
)]
public class SwapMidEntityFrameworkCoreTestModule : AbpModule
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

        ConfigureInMemorySqlite(context.Services);

        context.Services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment());

        context.Services.AddSingleton<FakeNotificationService>();
        context.Services.Replace(ServiceDescriptor.Singleton<INotificationService>(
            sp => sp.GetRequiredService<FakeNotificationService>()));

        context.Services.AddSingleton<FakeEtlPipelineRunner>();
        context.Services.Replace(ServiceDescriptor.Singleton<IEtlPipelineRunner>(
            sp => sp.GetRequiredService<FakeEtlPipelineRunner>()));

        context.Services.AddSingleton<FakeEtlTargetReceiptStore>();
        context.Services.Replace(ServiceDescriptor.Singleton<IEtlTargetReceiptStore>(
            sp => sp.GetRequiredService<FakeEtlTargetReceiptStore>()));

        context.Services.AddSingleton<TestDataPipelineExecutionFence>();
        context.Services.Replace(ServiceDescriptor.Singleton<IDataPipelineExecutionFence>(
            sp => sp.GetRequiredService<TestDataPipelineExecutionFence>()));

        context.Services.AddSingleton<TestPipelineTerminalStoreTopologyGuard>();
        context.Services.Replace(ServiceDescriptor.Singleton<IPipelineTerminalStoreTopologyGuard>(
            sp => sp.GetRequiredService<TestPipelineTerminalStoreTopologyGuard>()));

        context.Services.Replace(ServiceDescriptor.Singleton<
            IPipelineTerminalUnitOfWorkGuard,
            TestPipelineTerminalUnitOfWorkGuard>());

        context.Services.AddSingleton<MessageDeliveryRecorder>();
        context.Services.Replace(ServiceDescriptor.Singleton<
            IMessageDeliveryEndpointPolicy,
            FakeMessageDeliveryEndpointPolicy>());

        context.Services.AddSingleton<FakeMessageDeliveryScheduler>();
        context.Services.Replace(ServiceDescriptor.Singleton<IMessageDeliveryScheduler>(
            sp => sp.GetRequiredService<FakeMessageDeliveryScheduler>()));

        context.Services.RemoveAll<IMessageDeliveryClient>();
        context.Services.AddTransient<IMessageDeliveryClient>(
            sp => new FakeMessageDeliveryClient(DeliveryType.Http, sp.GetRequiredService<MessageDeliveryRecorder>()));
        context.Services.AddTransient<IMessageDeliveryClient>(
            sp => new FakeMessageDeliveryClient(DeliveryType.RabbitMq, sp.GetRequiredService<MessageDeliveryRecorder>()));
        context.Services.AddTransient<IMessageDeliveryClient>(
            sp => new FakeMessageDeliveryClient(DeliveryType.BotWebhook, sp.GetRequiredService<MessageDeliveryRecorder>()));
    }

    private void ConfigureInMemorySqlite(IServiceCollection services)
    {
        var useSeededTemplate = CanUseSeededTemplate(services);
        _sqliteConnection = useSeededTemplate
            ? SqliteTestDatabaseTemplates.CloneSeeded()
            : SqliteTestDatabaseTemplates.CloneSchemaOnly();

        if (useSeededTemplate)
        {
            services.AddSingleton(SeededTestDatabaseMarker.Instance);
        }

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

    private static bool CanUseSeededTemplate(IServiceCollection services)
    {
        if (services.GetSingletonInstanceOrNull<StandardTestHostConfigurationMarker>() == null)
        {
            return false;
        }

        var app = services.GetSingletonInstanceOrNull<IAbpApplication>();
        return app?.StartupModuleType == typeof(SwapMidEntityFrameworkCoreTestModule);
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

internal sealed class FakeMessageDeliveryEndpointPolicy : IMessageDeliveryEndpointPolicy
{
    public Uri Validate(DeliveryType deliveryType, string? endpointUrl)
        => new(endpointUrl ?? "https://example.test/", UriKind.Absolute);
}

internal sealed class TestPipelineTerminalUnitOfWorkGuard : IPipelineTerminalUnitOfWorkGuard
{
    public void EnsureTransactional()
    {
    }
}
