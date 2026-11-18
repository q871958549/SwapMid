using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SwapMid.SharedKernel.IntegrationEvents;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>
/// 真实 SQL Server Change Tracking CDC 合约测试（使用 Windows LocalDB，需设置 RUN_SQLSERVER_LOCALDB_CONTRACT_TESTS=1）。
/// 覆盖：DB/Table Change Tracking 预检、一致性全量快照、INSERT/UPDATE/DELETE CHANGETABLE 捕获、
/// 关系表字段重映射 + 软删除墓碑列及上游 Schema 漂移对比。
/// </summary>
public sealed class SqlServerCdcLocalDbContractTests
{
    [SqlServerLocalDbContractFact]
    [Trait("Category", "SqlServerContract")]
    public async Task ChangeTracking_Snapshot_Delta_SchemaDrift_And_SoftDelete_Mirror_Should_Work_On_LocalDb()
    {
        var dbName = $"SwapMidCtCdc_{Guid.NewGuid():N}";
        var masterConnStr = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
        }.ConnectionString;
        var dbConnStr = new SqlConnectionStringBuilder(masterConnStr)
        {
            InitialCatalog = dbName,
        }.ConnectionString;

        await ExecuteSqlAsync(masterConnStr, $"""
            CREATE DATABASE [{dbName}];
            ALTER DATABASE [{dbName}] SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON);
            ALTER DATABASE [{dbName}] SET ALLOW_SNAPSHOT_ISOLATION ON;
            """);

        try
        {
            await ExecuteSqlAsync(dbConnStr, """
                CREATE TABLE dbo.orders_ct_src (
                    src_order_id BIGINT NOT NULL PRIMARY KEY,
                    src_customer NVARCHAR(64) NOT NULL,
                    src_amount DECIMAL(14,2) NOT NULL,
                    internal_note NVARCHAR(64) NULL
                );
                ALTER TABLE dbo.orders_ct_src ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = OFF);

                INSERT INTO dbo.orders_ct_src (src_order_id, src_customer, src_amount, internal_note) VALUES
                    (3001, N'Alpha', 100.50, N'ignore-1'),
                    (3002, N'Beta', 200.00, N'ignore-2');

                CREATE TABLE dbo.orders_ct_mirror (
                    order_id BIGINT NOT NULL PRIMARY KEY,
                    customer_code NVARCHAR(64) NOT NULL,
                    net_amount DECIMAL(14,2) NOT NULL,
                    is_deleted BIT NOT NULL DEFAULT 0,
                    deleted_at_utc DATETIME2 NULL
                );
                """);

            var factory = new LocalDbConnectionFactory(dbConnStr);
            var ds = new DataSource(
                Guid.NewGuid(),
                "localdb-ct-src",
                DataSourceType.RelationalDatabase,
                $$"""{"provider":"sqlserver","host":"(localdb)\\MSSQLLocalDB","database":"{{dbName}}","integratedSecurity":true,"trustServerCertificate":true}""");

            const string mappingsJson =
                """[{"sourceColumn":"src_order_id","targetColumn":"order_id"},{"sourceColumn":"src_customer","targetColumn":"customer_code"},{"sourceColumn":"src_amount","targetColumn":"net_amount"}]""";

            var stream = new CdcStreamDefinition(
                Guid.NewGuid(),
                "sqlserver-ct-mirror",
                ds.Id,
                "sqlserver_ct",
                "ct_orders",
                CdcTargetMode.RelationalTable,
                monitoredTables: "dbo.orders_ct_src",
                targetSinkId: ds.Id,
                targetSinkDestination: "dbo.orders_ct_mirror",
                targetSinkKeyColumns: "order_id",
                initialSnapshotEnabled: true,
                targetDeletePolicy: CdcDeletePolicy.SoftDelete,
                schemaDriftPolicy: CdcSchemaDriftPolicy.PauseAndAlert,
                targetFieldMappingsJson: mappingsJson,
                softDeleteColumn: "is_deleted",
                softDeleteTimestampColumn: "deleted_at_utc",
                sourceType: CdcSourceType.SqlServerChangeTracking);

            var preflightSvc = new CdcRelationalMirrorPreflightService(factory);
            var mirrorCheck = await preflightSvc.EvaluateStreamAsync(ds, ds, stream, CancellationToken.None);
            mirrorCheck.Compatible.ShouldBeTrue(string.Join("; ", mirrorCheck.Issues.Select(i => i.Message)));

            var sqlCapture = new SqlServerCdcCaptureService(
                factory,
                new NoopPublisher(),
                null!,
                Array.Empty<IEtlSinkWriter>(),
                NullLogger<SqlServerCdcCaptureService>.Instance);

            var pf = await sqlCapture.PreflightAsync(ds, stream, CancellationToken.None);
            pf.IsReady.ShouldBeTrue(string.Join("; ", pf.Diagnostics));
            pf.WalLevel.ShouldBe("change_tracking");

            var snap = await sqlCapture.SnapshotAsync(ds, stream, ds, CancellationToken.None);
            snap.SnapshotRowCount.ShouldBe(2);
            stream.MarkSnapshotSucceeded(snap.ConsistentPointLsn, snap.SnapshotRowCount, snap.CompletedAtUtc);
            var schemaEval = await sqlCapture.EvaluateSchemaAsync(ds, stream, CancellationToken.None);
            stream.UpdateSchemaBaseline(schemaEval.CurrentHash, schemaEval.CurrentJson);

            // Emit CT INSERT (3003), UPDATE (3002), DELETE (3001)
            await ExecuteSqlAsync(dbConnStr, """
                DELETE FROM dbo.orders_ct_src WHERE src_order_id = 3001;
                INSERT INTO dbo.orders_ct_src (src_order_id, src_customer, src_amount, internal_note) VALUES (3003, N'Gamma', 330.75, N'x');
                UPDATE dbo.orders_ct_src SET src_amount = 240.25 WHERE src_order_id = 3002;
                """);

            var delta1 = await sqlCapture.CaptureBatchAsync(ds, stream, ds, CancellationToken.None);
            delta1.CapturedEventCount.ShouldBe(3);
            stream.MarkCaptureSucceeded(delta1.NewCheckpointLsn, delta1.CapturedEventCount, delta1.CapturedAtUtc);

            // Resurrect 3001
            await ExecuteSqlAsync(dbConnStr, """
                INSERT INTO dbo.orders_ct_src (src_order_id, src_customer, src_amount, internal_note) VALUES (3001, N'Alpha-Revived', 155.00, N'back');
                """);

            var delta2 = await sqlCapture.CaptureBatchAsync(ds, stream, ds, CancellationToken.None);
            delta2.CapturedEventCount.ShouldBe(1);
            stream.MarkCaptureSucceeded(delta2.NewCheckpointLsn, delta2.CapturedEventCount, delta2.CapturedAtUtc);

            await using (var verifyConn = new SqlConnection(dbConnStr))
            {
                await verifyConn.OpenAsync();
                await using var cmd = new SqlCommand(
                    "SELECT order_id, customer_code, net_amount, is_deleted, CASE WHEN deleted_at_utc IS NULL THEN 0 ELSE 1 END FROM dbo.orders_ct_mirror ORDER BY order_id",
                    verifyConn);
                await using var r = await cmd.ExecuteReaderAsync();
                var actual = new List<(long Id, string Cust, decimal Amt, bool Del, int HasTs)>();
                while (await r.ReadAsync())
                {
                    actual.Add((r.GetInt64(0), r.GetString(1), r.GetDecimal(2), r.GetBoolean(3), r.GetInt32(4)));
                }

                actual.ShouldBe(
                [
                    (3001L, "Alpha-Revived", 155.00m, false, 0),
                    (3002L, "Beta", 240.25m, false, 0),
                    (3003L, "Gamma", 330.75m, false, 0),
                ]);
            }

            // Delete 3003 cleanly as final tombstone verification
            await ExecuteSqlAsync(dbConnStr, "DELETE FROM dbo.orders_ct_src WHERE src_order_id = 3003;");
            var delta3 = await sqlCapture.CaptureBatchAsync(ds, stream, ds, CancellationToken.None);
            delta3.CapturedEventCount.ShouldBe(1);

            await using (var verifyConn = new SqlConnection(dbConnStr))
            {
                await verifyConn.OpenAsync();
                await using var cmd = new SqlCommand(
                    "SELECT is_deleted, CASE WHEN deleted_at_utc IS NOT NULL THEN 1 ELSE 0 END FROM dbo.orders_ct_mirror WHERE order_id = 3003",
                    verifyConn);
                await using var r = await cmd.ExecuteReaderAsync();
                (await r.ReadAsync()).ShouldBeTrue();
                r.GetBoolean(0).ShouldBeTrue();
                r.GetInt32(1).ShouldBe(1);
            }

            // Upstream DDL schema change detected cleanly
            await ExecuteSqlAsync(dbConnStr, "ALTER TABLE dbo.orders_ct_src ADD tier NVARCHAR(32) NULL;");
            var driftPf = await sqlCapture.PreflightAsync(ds, stream, CancellationToken.None);
            driftPf.SchemaDriftDetected.ShouldBeTrue();
            driftPf.SchemaDriftSummary.ShouldNotBeNull().ShouldContain("+col tier");
        }
        finally
        {
            await ExecuteSqlAsync(masterConnStr, $"""
                IF DB_ID(N'{dbName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{dbName}];
                END
                """);
        }
    }

    private static async Task ExecuteSqlAsync(string connStr, string sql)
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class LocalDbConnectionFactory(string connStr) : IEtlRelationalConnectionFactory
    {
        public DbConnection Create(string configJson, int connectTimeoutSeconds)
            => new SqlConnection(connStr);
    }

    private sealed class NoopPublisher : IIntegrationEventPublisher
    {
        public Task<bool> PublishIfTopicActiveAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
