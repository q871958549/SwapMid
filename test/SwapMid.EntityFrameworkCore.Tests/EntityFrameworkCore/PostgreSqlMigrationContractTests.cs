using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

namespace SwapMid.EntityFrameworkCore;

public class PostgreSqlMigrationContractTests
{
    public const string ConnectionEnvironmentVariable = "SWAPMID_POSTGRES_TEST_CONNECTION";

    private static SwapMidDbContext CreateHostContext(string connectionString) =>
        new(new DbContextOptionsBuilder<SwapMidDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory"))
            .Options);

    [PostgreSqlMigrationFact]
    public async Task Should_Apply_DbContext_Migrations_Twice_On_PostgreSql_18()
    {
        var adminConnectionString = RequireIsolatedConnectionString();
        var databaseName = $"swapmid_mig_{Guid.NewGuid():N}";
        var databaseConnectionString = WithDatabase(adminConnectionString, databaseName);

        await CreateDatabaseAsync(adminConnectionString, databaseName);

        try
        {
            await MigrateAsync(databaseConnectionString);
            await MigrateAsync(databaseConnectionString);

            await AssertDurableExecutionPartialIndexPlanAsync(databaseConnectionString);
        }
        finally
        {
            await DropDatabaseAsync(adminConnectionString, databaseName);
        }
    }

    private static async Task MigrateAsync(string connectionString)
    {
        await using var context = CreateHostContext(connectionString);
        var defined = context.Database.GetMigrations().ToList();
        defined.ShouldNotBeEmpty("SwapMidDbContext 至少需要一个显式迁移");

        await context.Database.MigrateAsync();

        var pending = await context.Database.GetPendingMigrationsAsync();
        pending.ShouldBeEmpty("迁移执行后不应残留待应用迁移");
    }

    private static async Task AssertDurableExecutionPartialIndexPlanAsync(
        string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var indexCmd = new NpgsqlCommand(
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'AppPipelineExecutions'
              AND indexname = 'IX_AppPipelineExecutions_Durable_StartedAt_Id'
            """,
            connection);
        var indexDef = (await indexCmd.ExecuteScalarAsync())?.ToString();
        indexDef.ShouldNotBeNull();
        indexDef.ShouldContain("WHERE");
    }

    private static string RequireIsolatedConnectionString()
    {
        var raw = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException($"Missing {ConnectionEnvironmentVariable}");
        }

        return raw;
    }

    private static string WithDatabase(string baseConnectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync();
        NpgsqlConnection.ClearAllPools();
        await using var cmd = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
            conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

public sealed class PostgreSqlMigrationFactAttribute : FactAttribute
{
    public PostgreSqlMigrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(PostgreSqlMigrationContractTests.ConnectionEnvironmentVariable)))
        {
            Skip = $"未设置 {PostgreSqlMigrationContractTests.ConnectionEnvironmentVariable}，跳过真实 PostgreSQL 迁移契约测试";
        }
    }
}
