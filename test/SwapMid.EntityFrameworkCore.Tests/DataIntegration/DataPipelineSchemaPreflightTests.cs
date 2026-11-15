using System;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
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
            ["postgresql", "sqlserver", "mysql", "rest-api", "file"]);
        capabilities.Where(c => c.SupportsTargetReceiptV1).Select(c => c.Provider).ShouldBe(
            ["postgresql", "sqlserver"]);
        capabilities.Where(c => c.SupportsTargetWrite).Select(c => c.Provider).ShouldBe(
            ["postgresql", "sqlserver", "mysql"]);
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

    private static DataPipelineSchemaPreflightAppService CreateService(
        IEtlRelationalConnectionFactory factory,
        DataSource source,
        DataSource target)
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
            NullLogger<DataPipelineSchemaPreflightAppService>.Instance)
        {
            LazyServiceProvider = lazyProvider,
        };
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
