using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.ApiManagement;
using SwapMid.DbMigrator;
using SwapMid.EnterpriseManagement;
using SwapMid.EntityFrameworkCore;
using SwapMid.Infrastructure;
using SwapMid.MessageBus;
using SwapMid.SystemManagement;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DbMigratorTests;

public class DbMigratorProductionSeedTests
{
    [Fact]
    public void Application_Assembly_Should_Have_Zero_SeedContributors()
    {
        var applicationContributors = typeof(SwapMidApplicationModule).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IDataSeedContributor).IsAssignableFrom(t))
            .Select(t => t.FullName!)
            .ToList();

        applicationContributors.ShouldBeEmpty(
            "SwapMid 的所有 IDataSeedContributor 应放在 Domain 或 DbMigrator 层，避免 DbMigrator 遗漏种子");
    }

    [Fact]
    public async Task DbMigrator_Production_Graph_Should_Seed_Cleanly_When_DemoSeed_Disabled()
    {
        using var connection = SqliteTestDatabaseTemplates.CloneSchemaOnly();
        DbMigratorTestHostModule.BootstrapConnection = connection;
        DbMigratorTestHostModule.DemoSeedEnabled = false;
        try
        {
            using var app = await AbpApplicationFactory.CreateAsync<DbMigratorTestHostModule>(
                options => options.UseAutofac());
            await app.InitializeAsync();

            using (var scope = app.ServiceProvider.CreateScope())
            {
                var sp = scope.ServiceProvider;
                var seeder = sp.GetRequiredService<IDataSeeder>();

                await seeder.SeedAsync();
                await seeder.SeedAsync();

                var uowManager = sp.GetRequiredService<IUnitOfWorkManager>();
                using var uow = uowManager.Begin();

                var enterprises = await sp.GetRequiredService<IRepository<Enterprise, Guid>>().GetListAsync();
                enterprises.ShouldBeEmpty();

                var topics = await sp.GetRequiredService<IRepository<MessageTopic, Guid>>().GetListAsync();
                topics.ShouldContain(x => x.Name == MessageBusTopics.IntegrationPipelineEvents);

                var appManager = sp.GetRequiredService<IOpenIddictApplicationManager>();
                (await appManager.FindByClientIdAsync("SwapMid_App")).ShouldNotBeNull();
                (await appManager.FindByClientIdAsync("SwapMid_Swagger")).ShouldNotBeNull();

                await uow.CompleteAsync();
            }

            await app.ShutdownAsync();
        }
        finally
        {
            DbMigratorTestHostModule.BootstrapConnection = null;
        }
    }

    [Fact]
    public async Task DbMigrator_Should_Seed_Demo_CodeRules_And_ApiRoutes_When_DemoSeed_Enabled()
    {
        using var connection = SqliteTestDatabaseTemplates.CloneSchemaOnly();
        DbMigratorTestHostModule.BootstrapConnection = connection;
        DbMigratorTestHostModule.DemoSeedEnabled = true;
        try
        {
            using var app = await AbpApplicationFactory.CreateAsync<DbMigratorTestHostModule>(
                options => options.UseAutofac());
            await app.InitializeAsync();

            using (var scope = app.ServiceProvider.CreateScope())
            {
                var sp = scope.ServiceProvider;
                var seeder = sp.GetRequiredService<IDataSeeder>();
                await seeder.SeedAsync();
                await seeder.SeedAsync(); // idempotent

                var uowManager = sp.GetRequiredService<IUnitOfWorkManager>();
                using var uow = uowManager.Begin();

                var codeRules = await sp.GetRequiredService<IRepository<CodeRule, Guid>>().GetListAsync();
                codeRules.Count.ShouldBeGreaterThanOrEqualTo(10);

                var apiGroups = await sp.GetRequiredService<IRepository<ApiGroup, Guid>>().GetListAsync();
                apiGroups.ShouldContain(g => g.Name == "Platform Diagnostics");

                var apiRoutes = await sp.GetRequiredService<IRepository<ApiRoute, Guid>>().GetListAsync();
                apiRoutes.ShouldContain(r => r.Path == "/gw/demo/health");

                await uow.CompleteAsync();
            }

            await app.ShutdownAsync();
        }
        finally
        {
            DbMigratorTestHostModule.BootstrapConnection = null;
            DbMigratorTestHostModule.DemoSeedEnabled = false;
        }
    }

    [DependsOn(
        typeof(SwapMidDbMigratorModule),
        typeof(AbpEntityFrameworkCoreSqliteModule)
    )]
    private sealed class DbMigratorTestHostModule : AbpModule
    {
        [ThreadStatic]
        internal static SqliteConnection? BootstrapConnection;

        [ThreadStatic]
        internal static bool DemoSeedEnabled;

        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            PreConfigure<AbpSqliteOptions>(x => x.BusyTimeout = null);
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            var connection = BootstrapConnection
                ?? throw new InvalidOperationException("BootstrapConnection 未注入");

            context.Services.ReplaceConfiguration(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Environment"] = "Production",
                        ["DemoSeed:Enabled"] = DemoSeedEnabled ? "true" : "false",
                        ["OpenIddict:Applications:SwapMid_App:ClientId"] = "SwapMid_App",
                        ["OpenIddict:Applications:SwapMid_App:RootUrl"] = "https://app.swapmid.dev",
                        ["OpenIddict:Applications:SwapMid_Swagger:ClientId"] = "SwapMid_Swagger",
                        ["OpenIddict:Applications:SwapMid_Swagger:RootUrl"] = "https://api.swapmid.dev",
                    })
                    .Build());

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

            context.Services.AddAlwaysDisableUnitOfWorkTransaction();
            context.Services.AddSingleton<IHostEnvironment, FakeHostEnvironment>();

            Configure<AbpDbContextOptions>(options =>
            {
                options.Configure(ctx => ctx.DbContextOptions.UseSqlite(connection));
                options.Configure<IdentityDbContext>(ctx => ctx.DbContextOptions.UseSqlite(connection));
                options.Configure<PermissionManagementDbContext>(ctx => ctx.DbContextOptions.UseSqlite(connection));
                options.Configure<SettingManagementDbContext>(ctx => ctx.DbContextOptions.UseSqlite(connection));
                options.Configure<OpenIddictDbContext>(ctx => ctx.DbContextOptions.UseSqlite(connection));
                options.Configure<BackgroundJobsDbContext>(ctx => ctx.DbContextOptions.UseSqlite(connection));
            });

            Configure<AbpUnitOfWorkDefaultOptions>(options =>
            {
                options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled;
            });
        }
    }
}
