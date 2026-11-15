using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DataIntegration;

public class DataPipelineSchemaPreflightTests
{
    [Fact]
    public async Task Capabilities_catalog_registers_all_five_connectors_and_receipt_flags()
    {
        await using var db = await SqliteSchemaPair.CreateAsync();
        var sut = CreateService(db, CreateRelationalSource("source"), CreateRelationalSource("target"));

        var capabilities = await sut.GetCapabilitiesAsync();

        capabilities.Select(c => c.Provider).ShouldBe(
            ["postgresql", "sqlserver", "mysql", "rest-api", "file", "clickhouse", "s3-parquet", "kafka"]);
        capabilities.Where(c => c.SupportsTargetReceiptV1).Select(c => c.Provider).ShouldBe(
            ["postgresql", "sqlserver"]);
        capabilities.Where(c => c.SupportsTargetWrite).Select(c => c.Provider).ShouldBe(
            ["postgresql", "sqlserver", "mysql", "clickhouse", "s3-parquet", "kafka"]);
    }

    [Fact]
    public async Task Preflight_reports_compatible_for_valid_upsert_projection_and_unique_not_null_key()
    {
        await using var db = await SqliteSchemaPair.CreateAsync();
        await db.ExecuteSourceAsync(
            """
            CREATE TABLE src_customers (
                customer_code TEXT NOT NULL,
                display_name TEXT NOT NULL,
                updated_seq INTEGER NOT NULL
            );
            """);
        await db.ExecuteTargetAsync(
            """
            CREATE TABLE tgt_customers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_code TEXT NOT NULL UNIQUE,
                display_name TEXT NOT NULL,
                updated_seq INTEGER NOT NULL,
                created_note TEXT DEFAULT 'seed'
            );
            """);

        var source = CreateRelationalSource("source");
        var target = CreateRelationalSource("target");
        var sut = CreateService(db, source, target);

        var res = await sut.PreflightSchemaAsync(new PreflightPipelineSchemaInput
        {
            SourceId = source.Id,
            TargetId = target.Id,
            TransformConfig = JsonSerializer.Serialize(new
            {
                sourceQuery = "SELECT customer_code, display_name, updated_seq FROM src_customers",
                targetTable = "tgt_customers",
                mode = "upsert",
                keyColumns = new[] { "customer_code" },
                watermarkColumn = "updated_seq",
            }),
        });

        res.Compatible.ShouldBeTrue();
        res.WriteMode.ShouldBe("upsert");
        res.SourceColumns.Count.ShouldBe(3);
        res.TargetColumns.Count.ShouldBe(5);
        res.Issues.Where(i => i.Severity == "error").ShouldBeEmpty();
    }

    [Fact]
    public async Task Preflight_detects_unmapped_source_missing_required_target_and_invalid_upsert_key()
    {
        await using var db = await SqliteSchemaPair.CreateAsync();
        await db.ExecuteSourceAsync(
            """
            CREATE TABLE src_bad (
                sku_code TEXT,
                ghost_column TEXT
            );
            """);
        await db.ExecuteTargetAsync(
            """
            CREATE TABLE tgt_bad (
                sku_code TEXT NULL,
                required_warehouse TEXT NOT NULL,
                qty INTEGER NOT NULL DEFAULT 0
            );
            """);

        var source = CreateRelationalSource("source");
        var target = CreateRelationalSource("target");
        var sut = CreateService(db, source, target);

        var res = await sut.PreflightSchemaAsync(new PreflightPipelineSchemaInput
        {
            SourceId = source.Id,
            TargetId = target.Id,
            TransformConfig = JsonSerializer.Serialize(new
            {
                sourceQuery = "SELECT sku_code, ghost_column FROM src_bad",
                targetTable = "tgt_bad",
                mode = "upsert",
                keyColumns = new[] { "sku_code" },
            }),
        });

        res.Compatible.ShouldBeFalse();
        var codes = res.Issues.Select(i => i.Code).ToHashSet(StringComparer.Ordinal);
        codes.ShouldContain("SwapMid:ETL:UnmappedSourceColumn");
        codes.ShouldContain("SwapMid:ETL:MissingRequiredTargetColumn");
        codes.ShouldContain("SwapMid:ETL:UpsertKeyNullable");
        codes.ShouldContain("SwapMid:ETL:UpsertKeyNotUniqueIndexed");
    }

    [Fact]
    public async Task Preflight_rejects_unsafe_non_select_source_query_before_opening_connection()
    {
        var factory = Substitute.For<IEtlRelationalConnectionFactory>();
        var source = CreateRelationalSource("source");
        var target = CreateRelationalSource("target");
        var sut = CreateService(factory, source, target);

        var res = await sut.PreflightSchemaAsync(new PreflightPipelineSchemaInput
        {
            SourceId = source.Id,
            TargetId = target.Id,
            TransformConfig = JsonSerializer.Serialize(new
            {
                sourceQuery = "DELETE FROM src_customers",
                targetTable = "tgt_customers",
                mode = "append",
            }),
        });

        res.Compatible.ShouldBeFalse();
        res.Issues.ShouldContain(i => i.Code == "SwapMid:ETL:SourceQueryUnsafe");
        factory.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public async Task Preflight_validates_explicit_column_mappings_for_renamed_and_projected_columns()
    {
        await using var db = await SqliteSchemaPair.CreateAsync();
        await db.ExecuteSourceAsync(
            """
            CREATE TABLE src_orders (
                order_no TEXT NOT NULL,
                gross_amt REAL NOT NULL,
                internal_note TEXT NULL
            );
            """);
        await db.ExecuteTargetAsync(
            """
            CREATE TABLE tgt_orders (
                order_code TEXT NOT NULL UNIQUE,
                amount_cny REAL NOT NULL,
                created_tag TEXT NOT NULL DEFAULT 'v1'
            );
            """);

        var source = CreateRelationalSource("source");
        var target = CreateRelationalSource("target");
        var sut = CreateService(db, source, target);

        var mapped = await sut.PreflightSchemaAsync(new PreflightPipelineSchemaInput
        {
            SourceId = source.Id,
            TargetId = target.Id,
            TransformConfig = JsonSerializer.Serialize(new
            {
                sourceQuery = "SELECT order_no, gross_amt, internal_note FROM src_orders",
                targetTable = "tgt_orders",
                mode = "upsert",
                keyColumns = new[] { "order_code" },
                columnMappings = new[]
                {
                    new { sourceColumn = "order_no", targetColumn = "order_code" },
                    new { sourceColumn = "gross_amt", targetColumn = "amount_cny" },
                },
            }),
        });

        mapped.Compatible.ShouldBeTrue();
        mapped.Issues.Where(i => i.Severity == "error").ShouldBeEmpty();

        var invalidMapping = await sut.PreflightSchemaAsync(new PreflightPipelineSchemaInput
        {
            SourceId = source.Id,
            TargetId = target.Id,
            TransformConfig = JsonSerializer.Serialize(new
            {
                sourceQuery = "SELECT order_no, gross_amt FROM src_orders",
                targetTable = "tgt_orders",
                mode = "append",
                columnMappings = new[]
                {
                    new { sourceColumn = "missing_src", targetColumn = "order_code" },
                    new { sourceColumn = "gross_amt", targetColumn = "missing_tgt" },
                },
            }),
        });

        invalidMapping.Compatible.ShouldBeFalse();
        var codes = invalidMapping.Issues.Select(i => i.Code).ToHashSet(StringComparer.Ordinal);
        codes.ShouldContain("SwapMid:ETL:MappedSourceMissing");
        codes.ShouldContain("SwapMid:ETL:MappedTargetMissing");
    }

    [Fact]
    public async Task Preflight与Runner_ClickHouse与ObjectStorage目标_支持结构自省及真实SQLite流式Parquet传输()
    {
        await using var sqlite = await SqliteSchemaPair.CreateAsync();
        await sqlite.ExecuteSourceAsync("""
            CREATE TABLE src_orders (
                id INTEGER PRIMARY KEY,
                customer_name TEXT NOT NULL,
                total_amount NUMERIC
            );
            INSERT INTO src_orders (id, customer_name, total_amount) VALUES (1, 'Alice', 42.5), (2, 'Bob', 100.0);
            """);

        byte[]? uploadedParquet = null;
        string? uploadedMethod = null;
        string? uploadedPath = null;
        var multiHandler = new RoutingHttpHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("lake"))
            {
                uploadedMethod = request.Method.Method;
                uploadedPath = request.RequestUri.AbsolutePath;
                uploadedParquet = request.Content is not null
                    ? await request.Content.ReadAsByteArrayAsync()
                    : null;
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK);
            }

            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent("""
                    {
                      "data": [
                        ["id", "Int64", 1, ""],
                        ["customer", "String", 0, ""],
                        ["amount", "Nullable(Decimal(18, 2))", 0, ""]
                      ]
                    }
                    """, System.Text.Encoding.UTF8, "application/json"),
            };
        });

        var probeConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SwapMid.Infrastructure.ExternalHttpConfigurationKeys.DataIntegrationSinkAllowedHosts] = "8.8.8.8",
            })
            .Build();
        var httpFactory = new InlineHttpClientFactory(multiHandler);
        var probeService = new DataIntegrationSinkProbeService(httpFactory, probeConfig);

        var source = CreateRelationalSource("source");
        var chTarget = new DataSource(
            Guid.NewGuid(),
            "ch-target",
            DataSourceType.ClickHouse,
            """{"host":"8.8.8.8","port":8123,"database":"analytics","username":"etl","targetResourceGroup":"rg-ch"}""");
        var chService = CreateService(sqlite, source, chTarget, probeService);

        var chPreflight = await chService.PreflightSchemaAsync(new PreflightPipelineSchemaInput
        {
            SourceId = source.Id,
            TargetId = chTarget.Id,
            TransformConfig = """
                {
                  "sourceQuery": "SELECT id, customer_name, total_amount FROM src_orders",
                  "targetTable": "orders_fact",
                  "mode": "append",
                  "columnMappings": [
                    { "sourceColumn": "id", "targetColumn": "id" },
                    { "sourceColumn": "customer_name", "targetColumn": "customer" },
                    { "sourceColumn": "total_amount", "targetColumn": "amount" }
                  ]
                }
                """,
        });

        chPreflight.Compatible.ShouldBeTrue();
        chPreflight.TargetProvider.ShouldBe("clickhouse");
        chPreflight.TargetColumns.Select(c => c.Name).ShouldBe(new[] { "id", "customer", "amount" });

        var s3Target = new DataSource(
            Guid.NewGuid(),
            "s3-target",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"https://8.8.8.8:9000","region":"us-east-1","bucket":"lake","prefix":"ods","format":"parquet","accessKey":"AK","secretKey":"SK","targetResourceGroup":"rg-s3"}""");
        var s3Service = CreateService(sqlite, source, s3Target, probeService);

        var s3TransformJson = """
            {
              "sourceQuery": "SELECT id, customer_name FROM src_orders ORDER BY id",
              "targetTable": "orders-v1",
              "mode": "append",
              "columnMappings": [
                { "sourceColumn": "id", "targetColumn": "order_id" },
                { "sourceColumn": "customer_name", "targetColumn": "buyer" }
              ]
            }
            """;

        var s3Preflight = await s3Service.PreflightSchemaAsync(new PreflightPipelineSchemaInput
        {
            SourceId = source.Id,
            TargetId = s3Target.Id,
            TransformConfig = s3TransformJson,
        });

        s3Preflight.Compatible.ShouldBeTrue();
        s3Preflight.TargetProvider.ShouldBe("s3-parquet");
        s3Preflight.TargetColumns.Select(c => c.Name).ShouldBe(new[] { "order_id", "buyer" });

        var runner = new EtlPipelineRunner(
            NullLogger<EtlPipelineRunner>.Instance,
            sqlite,
            sinkWriters: new IEtlSinkWriter[]
            {
                new ObjectStorageEtlSinkWriter(httpFactory, probeConfig),
                new ClickHouseEtlSinkWriter(httpFactory, probeConfig),
            });

        var runResult = await runner.RunAsync(source, s3Target, s3TransformJson);
        runResult.Rows.ShouldBe(2);
        runResult.TargetTruncated.ShouldBeFalse();
        uploadedMethod.ShouldBe("PUT");
        uploadedPath.ShouldStartWith("/lake/ods/orders-v1/run_");
        uploadedPath.ShouldEndWith(".parquet");
        uploadedParquet.ShouldNotBeNull();
        System.Text.Encoding.ASCII.GetString(uploadedParquet!, 0, 4).ShouldBe("PAR1");
    }

    private static DataPipelineSchemaPreflightAppService CreateService(
        IEtlRelationalConnectionFactory factory,
        DataSource source,
        DataSource target,
        DataIntegrationSinkProbeService? sinkProbe = null)
    {
        var repo = Substitute.For<IRepository<DataSource, Guid>>();
        repo.GetAsync(source.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(source);
        repo.GetAsync(target.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(target);

        var uow = Substitute.For<IUnitOfWork>();
        var uowManager = Substitute.For<IUnitOfWorkManager>();
        uowManager.Begin(Arg.Any<AbpUnitOfWorkOptions>(), Arg.Any<bool>()).Returns(uow);

        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.Id.Returns((Guid?)null);
        var lazyProvider = Substitute.For<IAbpLazyServiceProvider>();
        lazyProvider.LazyGetRequiredService<ICurrentTenant>().Returns(currentTenant);

        return new DataPipelineSchemaPreflightAppService(
            repo,
            factory,
            uowManager,
            NullLogger<DataPipelineSchemaPreflightAppService>.Instance,
            sinkProbe)
        {
            LazyServiceProvider = lazyProvider,
        };
    }

    private sealed class RoutingHttpHandler(
        Func<System.Net.Http.HttpRequestMessage, Task<System.Net.Http.HttpResponseMessage>> responder)
        : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken)
            => responder(request);
    }

    private sealed class InlineHttpClientFactory(System.Net.Http.HttpMessageHandler handler) : System.Net.Http.IHttpClientFactory
    {
        public System.Net.Http.HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static DataSource CreateRelationalSource(string dbName) =>
        new(
            Guid.NewGuid(),
            dbName,
            DataSourceType.RelationalDatabase,
            JsonSerializer.Serialize(new
            {
                provider = "postgresql",
                host = "db.partner.example",
                port = 5432,
                database = dbName,
                username = "etl_reader",
                password = "secret",
                encrypt = true,
                trustServerCertificate = false,
            }));

    private sealed class SqliteSchemaPair : IEtlRelationalConnectionFactory, IAsyncDisposable
    {
        private readonly string _srcCs;
        private readonly string _tgtCs;
        private readonly SqliteConnection _srcAnchor;
        private readonly SqliteConnection _tgtAnchor;

        private SqliteSchemaPair(string srcCs, string tgtCs, SqliteConnection srcAnchor, SqliteConnection tgtAnchor)
        {
            _srcCs = srcCs;
            _tgtCs = tgtCs;
            _srcAnchor = srcAnchor;
            _tgtAnchor = tgtAnchor;
        }

        public static async Task<SqliteSchemaPair> CreateAsync()
        {
            var id = Guid.NewGuid().ToString("N");
            var srcCs = $"Data Source=preflight-src-{id};Mode=Memory;Cache=Shared";
            var tgtCs = $"Data Source=preflight-tgt-{id};Mode=Memory;Cache=Shared";
            var s = new SqliteConnection(srcCs);
            var t = new SqliteConnection(tgtCs);
            await s.OpenAsync();
            await t.OpenAsync();
            return new SqliteSchemaPair(srcCs, tgtCs, s, t);
        }

        public DbConnection Create(string configJson, int connectTimeoutSeconds)
        {
            using var doc = JsonDocument.Parse(configJson);
            var db = doc.RootElement.GetProperty("database").GetString();
            return new SqliteConnection(db == "source" ? _srcCs : _tgtCs);
        }

        public DbConnection CreateTarget(string configJson, int connectTimeoutSeconds, int commandTimeoutSeconds)
            => Create(configJson, connectTimeoutSeconds);

        public Task ExecuteSourceAsync(string sql) => _srcAnchor.ExecuteAsync(sql);

        public Task ExecuteTargetAsync(string sql) => _tgtAnchor.ExecuteAsync(sql);

        public async ValueTask DisposeAsync()
        {
            await _srcAnchor.DisposeAsync();
            await _tgtAnchor.DisposeAsync();
        }
    }
}
