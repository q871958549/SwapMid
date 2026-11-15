using System;
using System.Data.Common;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class EtlPipelineUpsertTests
{
    [Fact]
    public async Task Sqlite_Upsert_插入新行_更新变更行_按水位线跳过陈旧行_保留未触碰行()
    {
        await using var db = await SqliteUpsertPair.CreateAsync();
        await db.ExecuteSourceAsync(
            """
            CREATE TABLE src_orders (
                order_id INTEGER NOT NULL PRIMARY KEY,
                customer TEXT NOT NULL,
                amount INTEGER NOT NULL,
                updated_seq INTEGER NOT NULL
            );
            INSERT INTO src_orders VALUES
                (1, 'Alice-New', 150, 20),
                (2, 'Bob-Stale', 50,  5),
                (3, 'Charlie',   300, 10);
            """);
        await db.ExecuteTargetAsync(
            """
            CREATE TABLE tgt_orders (
                order_id INTEGER NOT NULL PRIMARY KEY,
                customer TEXT NOT NULL,
                amount INTEGER NOT NULL,
                updated_seq INTEGER NOT NULL
            );
            INSERT INTO tgt_orders VALUES
                (1, 'Alice-Old', 100, 10),
                (2, 'Bob-Fresh', 200, 15),
                (99, 'Untouched', 999, 1);
            """);

        var runner = new EtlPipelineRunner(NullLogger<EtlPipelineRunner>.Instance, db);
        var transform = JsonSerializer.Serialize(new
        {
            sourceQuery = "SELECT order_id, customer, amount, updated_seq FROM src_orders ORDER BY order_id",
            targetTable = "tgt_orders",
            mode = "upsert",
            keyColumns = new[] { "order_id" },
            watermarkColumn = "updated_seq",
        });

        var result = await runner.RunAsync(
            CreateDataSource("source"),
            CreateDataSource("target"),
            transform);

        result.Rows.ShouldBe(3);
        result.TargetTruncated.ShouldBeFalse();
        (await db.TargetScalarAsync<long>("SELECT COUNT(*) FROM tgt_orders")).ShouldBe(4);
        (await db.TargetScalarAsync<string>("SELECT customer FROM tgt_orders WHERE order_id = 1")).ShouldBe("Alice-New");
        (await db.TargetScalarAsync<long>("SELECT amount FROM tgt_orders WHERE order_id = 1")).ShouldBe(150);
        // order_id=2 源水位线(5) < 目标水位线(15)，必须跳过覆盖
        (await db.TargetScalarAsync<string>("SELECT customer FROM tgt_orders WHERE order_id = 2")).ShouldBe("Bob-Fresh");
        (await db.TargetScalarAsync<long>("SELECT amount FROM tgt_orders WHERE order_id = 2")).ShouldBe(200);
        // order_id=3 新增；order_id=99 保留
        (await db.TargetScalarAsync<string>("SELECT customer FROM tgt_orders WHERE order_id = 3")).ShouldBe("Charlie");
        (await db.TargetScalarAsync<string>("SELECT customer FROM tgt_orders WHERE order_id = 99")).ShouldBe("Untouched");
    }

    [Fact]
    public async Task Sqlite_Upsert_源批次含重复键时必须在开启正式表事务前拒绝()
    {
        await using var db = await SqliteUpsertPair.CreateAsync();
        await db.ExecuteSourceAsync(
            """
            CREATE TABLE src_dup (code TEXT NOT NULL, qty INTEGER NOT NULL);
            INSERT INTO src_dup VALUES ('SKU-1', 10), ('SKU-1', 20);
            """);
        await db.ExecuteTargetAsync(
            """
            CREATE TABLE tgt_stock (code TEXT NOT NULL PRIMARY KEY, qty INTEGER NOT NULL);
            INSERT INTO tgt_stock VALUES ('SKU-0', 5);
            """);

        var runner = new EtlPipelineRunner(NullLogger<EtlPipelineRunner>.Instance, db);
        var transform = JsonSerializer.Serialize(new
        {
            sourceQuery = "SELECT code, qty FROM src_dup",
            targetTable = "tgt_stock",
            mode = "upsert",
            keyColumns = new[] { "code" },
        });

        var ex = await Should.ThrowAsync<BusinessException>(() => runner.RunAsync(
            CreateDataSource("source"),
            CreateDataSource("target"),
            transform));
        ex.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
        (await db.TargetScalarAsync<long>("SELECT COUNT(*) FROM tgt_stock")).ShouldBe(1);
    }

    [Fact]
    public async Task PostgreSql_Upsert_And_TargetUniqueConstraintPreflight_Work_Atomically()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SWAPMID_RUN_POSTGRES_CONTRACT_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var externalConnStr = Environment.GetEnvironmentVariable("SWAPMID_POSTGRES_TEST_CONNECTION");
        PostgreSqlContainer? ownedContainer = null;
        string baseConnStr;
        if (!string.IsNullOrWhiteSpace(externalConnStr))
        {
            baseConnStr = externalConnStr;
        }
        else
        {
            ownedContainer = new PostgreSqlBuilder("postgres:18-alpine").Build();
            await ownedContainer.StartAsync();
            baseConnStr = ownedContainer.GetConnectionString();
        }

        var dbName = $"swapmid_upsert_{Guid.NewGuid():N}";
        try
        {
            await using (var adminConn = new NpgsqlConnection(baseConnStr))
            {
                await adminConn.OpenAsync();
                await adminConn.ExecuteAsync($"CREATE DATABASE \"{dbName}\"");
            }

            var targetBuilder = new NpgsqlConnectionStringBuilder(baseConnStr)
            {
                Database = dbName,
            };
            await using (var setupConn = new NpgsqlConnection(targetBuilder.ConnectionString))
            {
                await setupConn.OpenAsync();
                await setupConn.ExecuteAsync(
                    """
                    CREATE TABLE public.src_items (
                        sku varchar(64) NOT NULL PRIMARY KEY,
                        title varchar(128) NOT NULL,
                        ver int NOT NULL
                    );
                    INSERT INTO public.src_items VALUES
                        ('A1', 'Widget Pro', 3),
                        ('B2', 'Stale Gadget', 1),
                        ('C3', 'New Sensor', 1);

                    CREATE TABLE public.tgt_items (
                        sku varchar(64) NOT NULL PRIMARY KEY,
                        title varchar(128) NOT NULL,
                        ver int NOT NULL
                    );
                    INSERT INTO public.tgt_items VALUES
                        ('A1', 'Widget Old', 1),
                        ('B2', 'Fresh Gadget', 5);

                    CREATE TABLE public.tgt_no_pk (
                        sku varchar(64) NOT NULL,
                        title varchar(128) NOT NULL
                    );
                    """);
            }

            var dsConfig = JsonSerializer.Serialize(new
            {
                provider = "postgresql",
                host = targetBuilder.Host,
                port = targetBuilder.Port,
                database = dbName,
                username = targetBuilder.Username,
                password = targetBuilder.Password,
                encrypt = false,
                trustServerCertificate = false,
                targetResourceGroup = "pg-upsert-test",
            });
            var sourceDs = new DataSource(Guid.NewGuid(), "pg-src", DataSourceType.RelationalDatabase, dsConfig);
            var targetDs = new DataSource(Guid.NewGuid(), "pg-tgt", DataSourceType.RelationalDatabase, dsConfig);

            var factory = new EtlRelationalConnectionFactory();
            var runner = new EtlPipelineRunner(NullLogger<EtlPipelineRunner>.Instance, factory);

            // 1. 无唯一键的目标表必须在 Preflight 拒绝
            var invalidNoPkTransform = JsonSerializer.Serialize(new
            {
                sourceQuery = "SELECT sku, title FROM public.src_items",
                targetTable = "public.tgt_no_pk",
                mode = "upsert",
                keyColumns = new[] { "sku" },
            });
            var noPkEx = await Should.ThrowAsync<BusinessException>(() =>
                runner.RunAsync(sourceDs, targetDs, invalidNoPkTransform));
            noPkEx.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);

            // 2. 合法唯一键 + 水位线 Upsert 原子生效
            var validTransform = JsonSerializer.Serialize(new
            {
                sourceQuery = "SELECT sku, title, ver FROM public.src_items ORDER BY sku",
                targetTable = "public.tgt_items",
                mode = "upsert",
                keyColumns = new[] { "sku" },
                watermarkColumn = "ver",
            });
            var runResult = await runner.RunAsync(sourceDs, targetDs, validTransform);
            runResult.Rows.ShouldBe(3);

            await using var verifyConn = new NpgsqlConnection(targetBuilder.ConnectionString);
            await verifyConn.OpenAsync();
            (await verifyConn.QuerySingleAsync<int>("SELECT COUNT(*) FROM public.tgt_items")).ShouldBe(3);
            (await verifyConn.QuerySingleAsync<string>("SELECT title FROM public.tgt_items WHERE sku = 'A1'")).ShouldBe("Widget Pro");
            (await verifyConn.QuerySingleAsync<string>("SELECT title FROM public.tgt_items WHERE sku = 'B2'")).ShouldBe("Fresh Gadget");
            (await verifyConn.QuerySingleAsync<string>("SELECT title FROM public.tgt_items WHERE sku = 'C3'")).ShouldBe("New Sensor");
        }
        finally
        {
            if (ownedContainer != null)
            {
                await ownedContainer.DisposeAsync();
            }
            else
            {
                await using var adminConn = new NpgsqlConnection(baseConnStr);
                await adminConn.OpenAsync();
                await adminConn.ExecuteAsync(
                    $"""
                    SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbName}';
                    DROP DATABASE IF EXISTS "{dbName}";
                    """);
            }
        }
    }

    private static DataSource CreateDataSource(string dbName) =>
        new(
            Guid.NewGuid(),
            dbName,
            DataSourceType.RelationalDatabase,
            JsonSerializer.Serialize(new
            {
                provider = "sqlserver",
                host = "127.0.0.1",
                port = 1433,
                database = dbName,
                username = "sa",
                password = "secret",
                targetResourceGroup = "sqlite-upsert-group",
            }));

    private sealed class SqliteUpsertPair : IEtlRelationalConnectionFactory, IAsyncDisposable
    {
        private readonly string _srcCs;
        private readonly string _tgtCs;
        private readonly SqliteConnection _srcAnchor;
        private readonly SqliteConnection _tgtAnchor;

        private SqliteUpsertPair(
            string srcCs,
            string tgtCs,
            SqliteConnection srcAnchor,
            SqliteConnection tgtAnchor)
        {
            _srcCs = srcCs;
            _tgtCs = tgtCs;
            _srcAnchor = srcAnchor;
            _tgtAnchor = tgtAnchor;
        }

        public static async Task<SqliteUpsertPair> CreateAsync()
        {
            var id = Guid.NewGuid().ToString("N");
            var srcCs = $"Data Source=upsert-src-{id};Mode=Memory;Cache=Shared";
            var tgtCs = $"Data Source=upsert-tgt-{id};Mode=Memory;Cache=Shared";
            var s = new SqliteConnection(srcCs);
            var t = new SqliteConnection(tgtCs);
            await s.OpenAsync();
            await t.OpenAsync();
            return new SqliteUpsertPair(srcCs, tgtCs, s, t);
        }

        public DbConnection Create(string configJson, int connectTimeoutSeconds)
        {
            using var doc = JsonDocument.Parse(configJson);
            var db = doc.RootElement.GetProperty("database").GetString();
            return new SqliteConnection(db == "source" ? _srcCs : _tgtCs);
        }

        public DbConnection CreateTarget(
            string configJson,
            int connectTimeoutSeconds,
            int commandTimeoutSeconds) => Create(configJson, connectTimeoutSeconds);

        public Task ExecuteSourceAsync(string sql) => _srcAnchor.ExecuteAsync(sql);

        public Task ExecuteTargetAsync(string sql) => _tgtAnchor.ExecuteAsync(sql);

        public Task<T> TargetScalarAsync<T>(string sql) => _tgtAnchor.ExecuteScalarAsync<T>(sql)!;

        public async ValueTask DisposeAsync()
        {
            await _srcAnchor.DisposeAsync();
            await _tgtAnchor.DisposeAsync();
        }
    }
}
