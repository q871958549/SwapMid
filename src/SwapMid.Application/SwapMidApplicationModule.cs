using System.Linq;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Account;
using Volo.Abp.Identity;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SwapMid.Infrastructure;
using SwapMid.Scheduling;
using Volo.Abp.RabbitMQ;
using Volo.Abp.TenantManagement;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Data;

namespace SwapMid;

[DependsOn(
    typeof(SwapMidDomainModule),
    typeof(SwapMidApplicationContractsModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpFeatureManagementApplicationModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpAccountApplicationModule),
    typeof(AbpDistributedLockingModule),
    typeof(AbpTenantManagementApplicationModule),
    typeof(AbpSettingManagementApplicationModule),
    typeof(AbpRabbitMqModule)
    )]
public class SwapMidApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        ConfigureExternalHttpClients(context, configuration);
        ConfigureScheduledJobHandlerCatalog(context);
        ConfigureEtlExecution(context.Services, configuration);

        Configure<AbpRabbitMqOptions>(options =>
        {
            options.Connections.Default.HostName = configuration["RabbitMQ:Host"] ?? "localhost";
            options.Connections.Default.Port = int.TryParse(configuration["RabbitMQ:Port"], out var port) ? port : 5672;
            options.Connections.Default.UserName = configuration["RabbitMQ:UserName"] ?? "guest";
            options.Connections.Default.Password = configuration["RabbitMQ:Password"] ?? "guest";
            options.Connections.Default.VirtualHost = configuration["RabbitMQ:VirtualHost"] ?? "/";
        });
    }

    private static void ConfigureExternalHttpClients(
        ServiceConfigurationContext context,
        IConfiguration configuration)
    {
        ExternalHttpClientRegistration.AddClients(context.Services, configuration);
    }

    private static void ConfigureScheduledJobHandlerCatalog(ServiceConfigurationContext context)
    {
        var handlerTypes = context.Services
            .Where(descriptor => descriptor.ServiceType == typeof(IScheduledJobHandler))
            .Select(descriptor => descriptor.ImplementationType)
            .Where(type => type != null)
            .Select(type => type!)
            .Distinct()
            .ToList();

        context.Services.AddSingleton(new ScheduledJobHandlerCatalog(handlerTypes));
    }

    internal static void ConfigureEtlExecution(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton<
            DataIntegration.PipelineTerminalStoreConnectionScopeAccessor>();
        services.RemoveAll<IConnectionStringResolver>();
        services.AddTransient<
            IConnectionStringResolver,
            DataIntegration.PipelinePinnedConnectionStringResolver>();

        services.AddTransient<
            DataIntegration.PipelineFailureNotificationJob>();
        services
            .AddOptions<DataIntegration.EtlExecutionOptions>()
            .Bind(configuration.GetSection(
                DataIntegration.EtlExecutionOptions.SectionName))
            .Validate(
                DataIntegration.EtlExecutionOptions.IsValid,
                DataIntegration.EtlExecutionOptions.ValidationMessage)
            .ValidateOnStart();
    }
}
