using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class EtlTargetReceiptStoreContractTests
{
    [Fact]
    public void Store接口必须是可注入的公开低敏边界()
    {
        typeof(IEtlTargetReceiptStore).IsPublic.ShouldBeTrue();
        typeof(EtlTargetReceiptProviderContext).IsPublic.ShouldBeTrue();
        typeof(EtlTargetReceiptActivation).IsPublic.ShouldBeTrue();
        typeof(IEtlTargetReceiptStore)
            .GetMethods()
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ShouldNotContain(
                type =>
                    type.FullName != null &&
                    type.FullName.Contains("RelationalDataSourceConnection+Probe", StringComparison.Ordinal),
                "公开 DI seam 不得泄漏内部配置解析类型");
    }

    [Theory]
    [InlineData("sqlserver", "swapmid_etl.TargetResourceV1", "target")]
    [InlineData("mssql", "SWAPMID_ETL.executionreceiptv1", "target")]
    [InlineData("postgresql", "swapmid_etl.target_resource_v1", "target")]
    [InlineData("postgres", "SWAPMID_ETL.EXECUTION_RECEIPT_V1", "target")]
    [InlineData("npgsql", "swapmid_etl.execution_receipt_v1", "target")]
    [InlineData("mysql", "target.swapmid_etl_target_resource_v1", "target")]
    [InlineData("mysql", "TARGET.SWAPMID_ETL_TARGET_RESOURCE_V1", "target")]
    [InlineData("mariadb", "target.swapmid_etl_execution_receipt_v1", "target")]
    public void 固定控制与回执表永远不得充当业务目标(
        string provider,
        string canonicalTable,
        string database)
    {
        var exception = Should.Throw<BusinessException>(() =>
            EtlTargetReceiptStore.EnsureBusinessTargetIsNotReserved(
                canonicalTable,
                new RelationalDataSourceConnection.Probe
                {
                    Provider = provider,
                    Database = database,
                },
                database));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
    }

    [Fact]
    public void 回执相等判断必须覆盖每个持久证据字段()
    {
        var evidence = ValidEvidence();
        EtlTargetReceiptStore.ReceiptEquals(evidence, evidence).ShouldBeTrue();

        var variants = new List<EtlTargetReceiptEvidence>
        {
            evidence with { RunId = Guid.NewGuid() },
            evidence with { PipelineId = Guid.NewGuid() },
            evidence with { ResourceInstanceId = Guid.NewGuid() },
            evidence with { LegacyTargetResourceHash = new string('0', PipelineExecutionConsts.Sha256HexLength) },
            evidence with { VerifiedTargetResourceHash = new string('1', PipelineExecutionConsts.Sha256HexLength) },
            evidence with { ExecutionContractHash = new string('2', PipelineExecutionConsts.Sha256HexLength) },
            evidence with { SourceQueryHash = new string('3', PipelineExecutionConsts.Sha256HexLength) },
            evidence with { TargetSchemaHash = new string('4', PipelineExecutionConsts.Sha256HexLength) },
            evidence with { BatchDigest = new string('5', PipelineExecutionConsts.Sha256HexLength) },
            evidence with { WriteMode = EtlWriteMode.Truncate },
            evidence with { ProcessedRows = evidence.ProcessedRows + 1 },
        };

        foreach (var variant in variants)
        {
            EtlTargetReceiptStore.ReceiptEquals(evidence, variant).ShouldBeFalse();
        }
    }

    [Fact]
    public void Provider实现不得创建或修补永久对象()
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration");
        var files = Directory.GetFiles(
            directory,
            "EtlTargetReceiptStore*.cs",
            SearchOption.TopDirectoryOnly);
        files.Length.ShouldBeGreaterThanOrEqualTo(3);

        var combined = string.Join(
            Environment.NewLine,
            files.Select(File.ReadAllText));
        combined.ShouldNotContain("CREATE SCHEMA", Case.Insensitive);
        combined.ShouldNotContain("CREATE TABLE", Case.Insensitive);
        combined.ShouldNotContain("ALTER TABLE", Case.Insensitive);
        combined.ShouldNotContain("DROP TABLE", Case.Insensitive);
        combined.ShouldNotContain("TRUNCATE TABLE", Case.Insensitive);
        combined.ShouldNotContain("MERGE ", Case.Insensitive);
        combined.ShouldNotContain("DELETE FROM", Case.Insensitive);
        combined.ShouldNotContain("UPDATE swapmid_etl", Case.Insensitive);
    }

    [Fact]
    public void Dba脚本必须固定精确协议对象与数据库约束()
    {
        var root = FindRepositoryRoot();
        var scriptDirectory = Path.Combine(
            root,
            "scripts",
            "data-integration",
            "target-receipt-v1");
        var sqlServer = File.ReadAllText(
            Path.Combine(scriptDirectory, "sqlserver.sql"));
        var mySql = File.ReadAllText(
            Path.Combine(scriptDirectory, "mysql.sql"));
        var postgreSql = File.ReadAllText(
            Path.Combine(scriptDirectory, "postgresql.sql"));

        sqlServer.ShouldContain("[swapmid_etl].[TargetResourceV1]");
        sqlServer.ShouldContain("[swapmid_etl].[ExecutionReceiptV1]");
        sqlServer.ShouldContain("PK_swapmid_etl_TargetResourceV1");
        sqlServer.ShouldContain("PK_swapmid_etl_ExecutionReceiptV1");
        sqlServer.ShouldContain("GRANT SELECT ON OBJECT::[swapmid_etl].[TargetResourceV1]");
        sqlServer.ShouldContain("GRANT SELECT, INSERT ON OBJECT::[swapmid_etl].[ExecutionReceiptV1]");

        mySql.ShouldContain("`swapmid_etl_target_resource_v1`");
        mySql.ShouldContain("`swapmid_etl_execution_receipt_v1`");
        mySql.ShouldContain("PK_swapmid_etl_target_resource_v1");
        mySql.ShouldContain("PK_swapmid_etl_execution_receipt_v1");

        postgreSql.ShouldContain("swapmid_etl.target_resource_v1");
        postgreSql.ShouldContain("swapmid_etl.execution_receipt_v1");
        postgreSql.ShouldContain("pk_swapmid_etl_target_resource_v1");
        postgreSql.ShouldContain("pk_swapmid_etl_execution_receipt_v1");
        postgreSql.ShouldContain("GRANT SELECT, INSERT ON TABLE swapmid_etl.execution_receipt_v1");
    }

    [Fact]
    public async Task InsertOrRead必须在数据库访问前拒绝错误连接事务与Provider()
    {
        var factory = new RecordingConnectionFactory();
        var store = new EtlTargetReceiptStore(factory);
        await using var fakeConnection = new UnsupportedDbConnection();
        await using var fakeTransaction = new UnsupportedDbTransaction(fakeConnection);
        var context = ValidContext();
        var evidence = ValidEvidence();

        await Should.ThrowAsync<BusinessException>(() =>
            store.InsertOrReadAsync(
                fakeConnection,
                fakeTransaction,
                context,
                evidence,
                commandTimeoutSeconds: 5,
                CancellationToken.None));

        await Should.ThrowAsync<BusinessException>(() =>
            store.InsertOrReadAsync(
                fakeConnection,
                new ForeignDbTransaction(),
                context,
                evidence,
                commandTimeoutSeconds: 5,
                CancellationToken.None));
    }

    [Theory]
    [InlineData("sqlserver", false, false)]
    [InlineData("sqlserver", true, true)]
    [InlineData("postgresql", false, false)]
    [InlineData("postgresql", true, true)]
    [InlineData("mysql", true, false)]
    public async Task Receipt目标必须在建连前拒绝非严格证书或未开放Provider(
        string provider,
        bool encrypt,
        bool trustServerCertificate)
    {
        var factory = new RecordingConnectionFactory();
        var store = new EtlTargetReceiptStore(factory);
        var port = provider.StartsWith("postgres", StringComparison.OrdinalIgnoreCase)
            ? 5432
            : 1433;
        var json = $$"""
            {
              "provider": "{{provider}}",
              "host": "db.internal",
              "port": {{port}},
              "database": "target_db",
              "username": "etl_writer",
              "password": "secret",
              "encrypt": {{encrypt.ToString().ToLowerInvariant()}},
              "trustServerCertificate": {{trustServerCertificate.ToString().ToLowerInvariant()}}
            }
            """;
        var target = new DataSource(
            Guid.NewGuid(),
            "target-ds",
            DataSourceType.RelationalDatabase,
            json);
        var transform = new EtlTransformConfig
        {
            SourceQuery = "SELECT 1 AS id",
            TargetTable = "orders",
            Mode = EtlWriteMode.Append,
            ReconciliationStrategy = EtlReconciliationStrategy.TargetReceiptV1,
        };

        var ex = await Should.ThrowAsync<BusinessException>(() =>
            store.ProbeActivationAsync(target, transform, CancellationToken.None));
        ex.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
        factory.CreateCalls.ShouldBe(0);
    }

    private static EtlTargetReceiptProviderContext ValidContext() =>
        new(
            "sqlserver",
            "SWAPMID_TARGET",
            new string('A', PipelineExecutionConsts.Sha256HexLength));

    private static EtlTargetReceiptEvidence ValidEvidence() =>
        new(
            Guid.Parse("01020304-0506-0708-090a-0b0c0d0e0f10"),
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            new string('B', PipelineExecutionConsts.Sha256HexLength),
            new string('C', PipelineExecutionConsts.Sha256HexLength),
            new string('D', PipelineExecutionConsts.Sha256HexLength),
            new string('E', PipelineExecutionConsts.Sha256HexLength),
            new string('F', PipelineExecutionConsts.Sha256HexLength),
            new string('9', PipelineExecutionConsts.Sha256HexLength),
            EtlWriteMode.Append,
            ProcessedRows: 3);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwapMid.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class UnsupportedDbConnection : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new UnsupportedDbTransaction(this);
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class UnsupportedDbTransaction(DbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        public override void Commit() { }
        public override void Rollback() { }
    }

    private sealed class ForeignDbTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel =>
            IsolationLevel.ReadCommitted;

        [AllowNull]
        protected override DbConnection? DbConnection => null;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }

    private sealed class RecordingConnectionFactory : IEtlRelationalConnectionFactory
    {
        public int CreateCalls { get; private set; }

        public DbConnection Create(string configJson, int connectTimeoutSeconds)
        {
            CreateCalls++;
            throw new NotSupportedException();
        }

        public DbConnection CreateTarget(
            string configJson,
            int connectTimeoutSeconds,
            int commandTimeoutSeconds)
        {
            CreateCalls++;
            throw new NotSupportedException();
        }
    }
}
