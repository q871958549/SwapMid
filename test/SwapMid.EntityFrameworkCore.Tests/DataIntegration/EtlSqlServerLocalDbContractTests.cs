using System;
using System.Data.Common;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>
/// 真实 SQL Server provider 合约。默认跳过；Windows/Gitea Runner 显式设置
/// RUN_SQLSERVER_LOCALDB_CONTRACT_TESTS=1 后使用独立临时数据库验证完整 ETL SQL。
/// </summary>
public sealed class EtlSqlServerLocalDbContractTests
{
    [SqlServerLocalDbContractFact]
    [Trait("Category", "SqlServerContract")]
    public async Task 原子替换保留高精度且有损类型与Graph目标在删除前失败()
    {
        var database =
            $"SwapMidEtlContract_{Guid.NewGuid():N}";
        var masterConnectionString =
            new SqlConnectionStringBuilder
            {
                DataSource = @"(localdb)\MSSQLLocalDB",
                InitialCatalog = "master",
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                ConnectTimeout = 5,
            }.ConnectionString;
        var databaseConnectionString =
            new SqlConnectionStringBuilder(
                masterConnectionString)
            {
                InitialCatalog = database,
            }.ConnectionString;

        await CreateDatabaseAsync(
            masterConnectionString,
            database);
        try
        {
            await CreateContractTablesAsync(
                databaseConnectionString);
            var runner = new EtlPipelineRunner(
                NullLogger<EtlPipelineRunner>.Instance,
                new LocalDbConnectionFactory(
                    databaseConnectionString));
            var source = CreateDataSource("source");
            var target = CreateDataSource("target");

            var result = await runner.RunAsync(
                source,
                target,
                Transform(
                    "SELECT id, amount, label, occurred_at " +
                    "FROM dbo.source_rows ORDER BY id",
                    "target_rows"));

            result.Rows.ShouldBe(2);
            result.TargetTruncated.ShouldBeTrue();
            (await ReadTargetRowsAsync(
                    databaseConnectionString,
                    "dbo.target_rows"))
                .ShouldBe(
                [
                    "1|1.25|alpha|2026-07-27T10:00:00.1234567",
                    "2|2.50|beta|2026-07-27T11:00:00.7654321",
                ]);

            await Should.ThrowAsync<SqlException>(() =>
                runner.RunAsync(
                    source,
                    target,
                    Transform(
                        "SELECT id, amount " +
                        "FROM dbo.source_rounding",
                        "target_rounding")));

            (await ReadTargetRowsAsync(
                    databaseConnectionString,
                    "dbo.target_rounding"))
                .ShouldBe(["99|9.99"]);

            var graphException =
                await Should.ThrowAsync<BusinessException>(() =>
                    runner.RunAsync(
                        source,
                        target,
                        Transform(
                            "SELECT name FROM dbo.source_graph",
                            "target_graph")));
            graphException.Code.ShouldBe(
                SwapMidDomainErrorCodes
                    .DataIntegration
                    .PipelineExecutionInputInvalid);
            (await ReadTargetRowsAsync(
                    databaseConnectionString,
                    "dbo.target_graph"))
                .ShouldBe(["old-graph"]);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(
                masterConnectionString,
                database);
        }
    }

    private static async Task CreateDatabaseAsync(
        string masterConnectionString,
        string database)
    {
        await using var connection =
            new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE DATABASE [{database}];
            ALTER DATABASE [{database}]
                SET ALLOW_SNAPSHOT_ISOLATION ON;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateContractTablesAsync(
        string connectionString)
    {
        await using var connection =
            new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE dbo.source_rows
            (
                id int NOT NULL,
                amount decimal(10, 2) NOT NULL,
                label nvarchar(20) NOT NULL,
                occurred_at datetime2(7) NOT NULL
            );
            CREATE TABLE dbo.target_rows
            (
                id int NOT NULL,
                amount decimal(10, 2) NOT NULL,
                label nvarchar(20) NOT NULL,
                occurred_at datetime2(7) NOT NULL
            );
            INSERT dbo.source_rows
                (id, amount, label, occurred_at)
            VALUES
                (1, 1.25, N'alpha', '2026-07-27T10:00:00.1234567'),
                (2, 2.50, N'beta', '2026-07-27T11:00:00.7654321');
            INSERT dbo.target_rows
                (id, amount, label, occurred_at)
            VALUES
                (99, 9.99, N'old', '2026-07-26T09:00:00.0000000');

            CREATE TABLE dbo.source_rounding
            (
                id int NOT NULL,
                amount decimal(10, 3) NOT NULL
            );
            CREATE TABLE dbo.target_rounding
            (
                id int NOT NULL,
                amount decimal(10, 2) NOT NULL
            );
            INSERT dbo.source_rounding (id, amount)
                VALUES (1, 1.234);
            INSERT dbo.target_rounding (id, amount)
                VALUES (99, 9.99);

            CREATE TABLE dbo.source_graph
            (
                name nvarchar(20) NOT NULL
            );
            CREATE TABLE dbo.target_graph
            (
                name nvarchar(20) NOT NULL
            ) AS NODE;
            INSERT dbo.source_graph (name)
                VALUES (N'new-graph');
            INSERT dbo.target_graph (name)
                VALUES (N'old-graph');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> ReadTargetRowsAsync(
        string connectionString,
        string table)
    {
        await using var connection =
            new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = table switch
        {
            "dbo.target_rounding" => """
                  SELECT CONCAT(
                      id,
                      '|',
                      CONVERT(varchar(32), amount))
                  FROM dbo.target_rounding
                  ORDER BY id
                  """,
            "dbo.target_graph" => """
                  SELECT name
                  FROM dbo.target_graph
                  ORDER BY name
                  """,
            _ => """
                  SELECT CONCAT(
                      id,
                      '|',
                      CONVERT(varchar(32), amount),
                      '|',
                      label,
                      '|',
                      CONVERT(varchar(33), occurred_at, 126))
                  FROM dbo.target_rows
                  ORDER BY id
                  """,
        };
        var rows = new System.Collections.Generic.List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows.ToArray();
    }

    private static async Task DropDatabaseAsync(
        string masterConnectionString,
        string database)
    {
        await using var connection =
            new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{database}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{database}]
                    SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{database}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static DataSource CreateDataSource(string database)
        => new(
            Guid.NewGuid(),
            database,
            DataSourceType.RelationalDatabase,
            JsonSerializer.Serialize(new
            {
                provider = "sqlserver",
                database,
            }));

    private static string Transform(
        string sourceQuery,
        string targetTable)
        => JsonSerializer.Serialize(new
        {
            sourceQuery,
            targetTable,
            mode = "truncate",
        });

    private sealed class LocalDbConnectionFactory(
        string connectionString)
        : IEtlRelationalConnectionFactory
    {
        public DbConnection Create(
            string configJson,
            int connectTimeoutSeconds)
            => new SqlConnection(connectionString);
    }
}

public sealed class SqlServerLocalDbContractFactAttribute
    : FactAttribute
{
    public SqlServerLocalDbContractFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(
                    "RUN_SQLSERVER_LOCALDB_CONTRACT_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            Skip =
                "设置 RUN_SQLSERVER_LOCALDB_CONTRACT_TESTS=1 后才运行 Windows LocalDB 契约测试。";
        }
    }
}
