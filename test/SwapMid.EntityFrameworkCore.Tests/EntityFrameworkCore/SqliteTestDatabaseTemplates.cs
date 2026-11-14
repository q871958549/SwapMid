using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SwapMid.Infrastructure;
using Volo.Abp;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.Uow;

namespace SwapMid.EntityFrameworkCore;

public static class SqliteTestDatabaseTemplates
{
    private static readonly Lazy<SqliteConnection> SchemaTemplate =
        new(BuildSchemaTemplate, isThreadSafe: true);

    private static readonly Lazy<SqliteConnection> SeededTemplate =
        new(BuildSeededTemplate, isThreadSafe: true);

    private static readonly object SchemaBackupLock = new();
    private static readonly object SeededBackupLock = new();

    public static SqliteConnection CloneSchemaOnly()
        => CloneFrom(SchemaTemplate.Value, SchemaBackupLock);

    public static SqliteConnection CloneSeeded()
        => CloneFrom(SeededTemplate.Value, SeededBackupLock);

    public static IReadOnlyList<string> ListUserTables(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var reader = cmd.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public static IReadOnlyDictionary<string, long> SnapshotRowCounts(SqliteConnection connection)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in ListUserTables(connection))
        {
            using var cmd = connection.CreateCommand();
            var escaped = table.Replace("\"", "\"\"");
            cmd.CommandText = $"SELECT COUNT(*) FROM \"{escaped}\";";
            counts[table] = (long)cmd.ExecuteScalar()!;
        }

        return counts;
    }

    internal static void CreateAllTables(SqliteConnection connection)
    {
        CreateTablesFor<SwapMidDbContext>(
            connection, o => new SwapMidDbContext(o));
    }

    private static SqliteConnection BuildSchemaTemplate()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        CreateAllTables(connection);
        return connection;
    }

    private static SqliteConnection BuildSeededTemplate()
    {
        var connection = CloneSchemaOnly();

        TemplateSeedBootstrapModule.BootstrapConnection = connection;
        try
        {
            using var app = AbpApplicationFactory.Create<TemplateSeedBootstrapModule>(options =>
            {
                options.UseAutofac();
            });
            app.Initialize();
            app.Shutdown();
        }
        finally
        {
            TemplateSeedBootstrapModule.BootstrapConnection = null;
        }

        return connection;
    }

    private static SqliteConnection CloneFrom(SqliteConnection source, object gate)
    {
        var target = new SqliteConnection("Data Source=:memory:");
        target.Open();

        lock (gate)
        {
            source.BackupDatabase(target);
        }

        return target;
    }

    private static void CreateTablesFor<TDbContext>(
        SqliteConnection connection,
        Func<DbContextOptions<TDbContext>, TDbContext> factory)
        where TDbContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = factory(options);
        context.GetService<IRelationalDatabaseCreator>().CreateTables();
    }

    [DependsOn(
        typeof(SwapMidApplicationTestModule),
        typeof(SwapMidEntityFrameworkCoreModule),
        typeof(AbpEntityFrameworkCoreSqliteModule)
    )]
    private sealed class TemplateSeedBootstrapModule : AbpModule
    {
        [ThreadStatic]
        internal static SqliteConnection? BootstrapConnection;

        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            PreConfigure<AbpSqliteOptions>(x => x.BusyTimeout = null);
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            var connection = BootstrapConnection
                ?? throw new InvalidOperationException(
                    "TemplateSeedBootstrapModule 仅允许由 SqliteTestDatabaseTemplates.BuildSeededTemplate 调用。");

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
