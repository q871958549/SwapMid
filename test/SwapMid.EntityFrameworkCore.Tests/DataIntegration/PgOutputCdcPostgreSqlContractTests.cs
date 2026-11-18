using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using SwapMid.SharedKernel.IntegrationEvents;
using Xunit;

namespace SwapMid.DataIntegration;

public sealed class PostgreSqlLogicalReplicationFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "SWAPMID_RUN_POSTGRES_CDC_CONTRACT_TESTS";

    public PostgreSqlLogicalReplicationFactAttribute()
    {
        var enabled = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.Equals(enabled, "1", StringComparison.Ordinal) &&
            !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Requires PostgreSQL wal_level=logical instance ({EnvironmentVariable}=1).";
        }
    }
}

public sealed class PgOutputCdcPostgreSqlContractTests
{
    private const string ConnectionEnvVar = "SWAPMID_POSTGRES_CDC_TEST_CONNECTION";

    [PostgreSqlLogicalReplicationFact]
    public async Task CaptureBatchAsync_Should_Capture_Insert_Update_Delete_And_Advance_ConfirmedFlushLsn()
    {
        var connStr = Environment.GetEnvironmentVariable(ConnectionEnvVar)
                      ?? Environment.GetEnvironmentVariable("SWAPMID_POSTGRES_TEST_CONNECTION");
        connStr.ShouldNotBeNullOrWhiteSpace("Missing PostgreSQL logical replication connection string.");

        var schemaTable = $"cdc_contract_{Guid.NewGuid():N}"[..24];
        var pubName = $"pub_{schemaTable}";
        var slotName = $"slot_{schemaTable}";

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var walLevel = Convert.ToString(await new NpgsqlCommand("SHOW wal_level", conn).ExecuteScalarAsync());
        walLevel.ShouldBe("logical");

        await ExecuteNonQueryAsync(conn, $"""
            CREATE TABLE public."{schemaTable}" (
                id bigint PRIMARY KEY,
                payload text NOT NULL
            );
            ALTER TABLE public."{schemaTable}" REPLICA IDENTITY FULL;
            """);

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connStr);
            var source = new DataSource(
                Guid.NewGuid(),
                "cdc-contract-pg",
                DataSourceType.RelationalDatabase,
                $$"""{"provider":"postgresql","host":"{{builder.Host}}","port":{{builder.Port}},"database":"{{builder.Database}}","username":"{{builder.Username}}","password":"{{builder.Password}}","encrypt":false,"trustServerCertificate":true}""");

            var stream = new CdcStreamDefinition(
                Guid.NewGuid(),
                "pgoutput-contract-stream",
                source.Id,
                pubName,
                slotName,
                CdcTargetMode.MessageBusTopic,
                targetTopicName: IntegrationTopicNames.CdcEvents,
                monitoredTables: $"public.{schemaTable}",
                autoCreatePublicationAndSlot: true);

            var publisher = new RecordingIntegrationEventPublisher();
            var service = new PgOutputCdcCaptureService(
                new EtlRelationalConnectionFactory(),
                publisher,
                null!,
                NullLogger<PgOutputCdcCaptureService>.Instance);

            var preflight1 = await service.PreflightAsync(source, stream, CancellationToken.None);
            preflight1.IsReady.ShouldBeTrue();
            preflight1.WalLevel.ShouldBe("logical");
            preflight1.TableReplicaIdentities.ShouldContain(
                x => x.Schema == "public" && x.Table == schemaTable && x.ReplicaIdentity == "full");

            // Initial capture provisions publication & slot at current WAL point.
            var initBatch = await service.CaptureBatchAsync(source, stream, CancellationToken.None);
            initBatch.CapturedEventCount.ShouldBe(0);
            stream.MarkCaptureSucceeded(initBatch.NewCheckpointLsn, initBatch.CapturedEventCount, initBatch.CapturedAtUtc);

            var unmonitoredTable = $"{schemaTable}_bg";
            await ExecuteNonQueryAsync(conn, $"""
                INSERT INTO public."{schemaTable}" (id, payload) VALUES (1, 'alpha');
                UPDATE public."{schemaTable}" SET payload = 'beta' WHERE id = 1;
                DELETE FROM public."{schemaTable}" WHERE id = 1;
                CREATE TEMP TABLE IF NOT EXISTS "{unmonitoredTable}" (v int);
                INSERT INTO "{unmonitoredTable}" VALUES (1);
                """);

            var batch = await service.CaptureBatchAsync(source, stream, CancellationToken.None);
            batch.CapturedEventCount.ShouldBe(3);
            batch.DeliveredCount.ShouldBe(3);
            batch.NewCheckpointLsn.ShouldNotBeNullOrWhiteSpace();

            var postPreflight = await service.PreflightAsync(source, stream, CancellationToken.None);
            postPreflight.SlotExists.ShouldBeTrue();
            postPreflight.ConfirmedFlushLsn.ShouldBe(batch.NewCheckpointLsn);
            postPreflight.WalLagBytes.ShouldNotBeNull();

            publisher.Envelopes.Select(e => e.Subject).ShouldBe(new[]
            {
                $"cdc.public.{schemaTable}.insert",
                $"cdc.public.{schemaTable}.update",
                $"cdc.public.{schemaTable}.delete",
            });
            publisher.Envelopes[1].PayloadJson.ShouldContain("\"changedColumns\":[\"payload\"]");
            publisher.Envelopes[1].PayloadJson.ShouldContain("\"alpha\"");

            CdcProtocolHelpers.CompareLsn(initBatch.NewCheckpointLsn, batch.NewCheckpointLsn).ShouldBeLessThanOrEqualTo(0);

            // Second capture at confirmed LSN must produce 0 duplicates.
            stream.MarkCaptureSucceeded(batch.NewCheckpointLsn, batch.CapturedEventCount, batch.CapturedAtUtc);
            var replayBatch = await service.CaptureBatchAsync(source, stream, CancellationToken.None);
            replayBatch.CapturedEventCount.ShouldBe(0);
            publisher.Envelopes.Count.ShouldBe(3);

            var dropResult = await service.DropSlotAsync(source, stream, CancellationToken.None);
            dropResult.Dropped.ShouldBeTrue();
            var afterDrop = await service.PreflightAsync(source, stream, CancellationToken.None);
            afterDrop.SlotExists.ShouldBeFalse();
        }
        finally
        {
            await ExecuteNonQueryAsync(conn, $"""
                SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_replication_slots WHERE slot_name = '{slotName}')
                    THEN pg_drop_replication_slot('{slotName}') END;
                DROP PUBLICATION IF EXISTS "{pubName}";
                DROP TABLE IF EXISTS public."{schemaTable}";
                """);
        }
    }

    [PostgreSqlLogicalReplicationFact]
    public async Task SnapshotAsync_WithExportSnapshot_Should_Capture_Baseline_And_Hand_Off_To_Wal_Without_Duplicates()
    {
        var connStr = Environment.GetEnvironmentVariable(ConnectionEnvVar)
                      ?? Environment.GetEnvironmentVariable("SWAPMID_POSTGRES_TEST_CONNECTION");
        connStr.ShouldNotBeNullOrWhiteSpace("Missing PostgreSQL logical replication connection string.");

        var schemaTable = $"cdc_snap_{Guid.NewGuid():N}"[..24];
        var pubName = $"pub_{schemaTable}";
        var slotName = $"slot_{schemaTable}";

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        await ExecuteNonQueryAsync(conn, $"""
            CREATE TABLE public."{schemaTable}" (
                id bigint PRIMARY KEY,
                payload text NOT NULL
            );
            ALTER TABLE public."{schemaTable}" REPLICA IDENTITY FULL;
            INSERT INTO public."{schemaTable}" (id, payload) VALUES (101, 'existing-a'), (102, 'existing-b');
            """);

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connStr);
            var source = new DataSource(
                Guid.NewGuid(),
                "cdc-snap-contract-pg",
                DataSourceType.RelationalDatabase,
                $$"""{"provider":"postgresql","host":"{{builder.Host}}","port":{{builder.Port}},"database":"{{builder.Database}}","username":"{{builder.Username}}","password":"{{builder.Password}}","encrypt":false,"trustServerCertificate":true}""");

            var stream = new CdcStreamDefinition(
                Guid.NewGuid(),
                "pgoutput-snapshot-contract",
                source.Id,
                pubName,
                slotName,
                CdcTargetMode.MessageBusTopic,
                targetTopicName: IntegrationTopicNames.CdcEvents,
                monitoredTables: $"public.{schemaTable}",
                autoCreatePublicationAndSlot: true,
                maxBatchSize: 1,
                initialSnapshotEnabled: true);

            var publisher = new RecordingIntegrationEventPublisher();
            var service = new PgOutputCdcCaptureService(
                new EtlRelationalConnectionFactory(),
                publisher,
                null!,
                NullLogger<PgOutputCdcCaptureService>.Instance);

            var snapshot = await service.SnapshotAsync(source, stream, null, CancellationToken.None);
            snapshot.UsedExportedSnapshot.ShouldBeTrue();
            snapshot.SnapshotRowCount.ShouldBe(2);
            snapshot.DeliveredCount.ShouldBe(2);
            snapshot.ConsistentPointLsn.ShouldNotBeNullOrWhiteSpace();
            stream.MarkSnapshotSucceeded(snapshot.ConsistentPointLsn, snapshot.SnapshotRowCount, snapshot.CompletedAtUtc);

            publisher.Envelopes.Count.ShouldBe(2);
            publisher.Envelopes.Select(e => e.Subject).ShouldAllBe(s => s == $"cdc.public.{schemaTable}.snapshot");
            publisher.Envelopes.Select(e => e.EventId).Distinct().Count().ShouldBe(2);

            await ExecuteNonQueryAsync(conn, $"""
                INSERT INTO public."{schemaTable}" (id, payload) VALUES (103, 'delta-c');
                UPDATE public."{schemaTable}" SET payload = 'existing-a-v2' WHERE id = 101;
                """);

            var delta = await service.CaptureBatchAsync(source, stream, CancellationToken.None);
            delta.CapturedEventCount.ShouldBe(2);
            publisher.Envelopes.Count.ShouldBe(4);
            publisher.Envelopes[2].Subject.ShouldBe($"cdc.public.{schemaTable}.insert");
            publisher.Envelopes[3].Subject.ShouldBe($"cdc.public.{schemaTable}.update");
        }
        finally
        {
            await ExecuteNonQueryAsync(conn, $"""
                SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_replication_slots WHERE slot_name = '{slotName}')
                    THEN pg_drop_replication_slot('{slotName}') END;
                DROP PUBLICATION IF EXISTS "{pubName}";
                DROP TABLE IF EXISTS public."{schemaTable}";
                """);
        }
    }

    [PostgreSqlLogicalReplicationFact]
    public async Task RelationalMirror_And_SchemaDriftGuard_Should_Work_EndToEnd_On_PostgreSql()
    {
        var connStr = Environment.GetEnvironmentVariable(ConnectionEnvVar)
                      ?? Environment.GetEnvironmentVariable("SWAPMID_POSTGRES_TEST_CONNECTION");
        connStr.ShouldNotBeNullOrWhiteSpace("Missing PostgreSQL logical replication connection string.");

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var srcTable = $"cdc_m_src_{suffix}";
        var tgtTable = $"cdc_m_tgt_{suffix}";
        var pubName = $"pub_{suffix}";
        var slotName = $"slot_{suffix}";

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        await ExecuteNonQueryAsync(conn, $"""
            CREATE TABLE public."{srcTable}" (
                order_id bigint PRIMARY KEY,
                customer_name text NOT NULL,
                total_amount numeric(14,2) NOT NULL
            );
            ALTER TABLE public."{srcTable}" REPLICA IDENTITY FULL;
            INSERT INTO public."{srcTable}" (order_id, customer_name, total_amount) VALUES (1, 'Alice', 100.50);

            CREATE TABLE public."{tgtTable}" (
                order_id bigint PRIMARY KEY,
                customer_name text NOT NULL,
                total_amount numeric(14,2) NOT NULL
            );
            INSERT INTO public."{tgtTable}" (order_id, customer_name, total_amount) VALUES (99, 'Stale', 1.00);
            """);

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connStr);
            var dsJson = $$"""{"provider":"postgresql","host":"{{builder.Host}}","port":{{builder.Port}},"database":"{{builder.Database}}","username":"{{builder.Username}}","password":"{{builder.Password}}","encrypt":false,"trustServerCertificate":true}""";
            var pgDs = new DataSource(Guid.NewGuid(), "cdc-mirror-pg", DataSourceType.RelationalDatabase, dsJson);

            var stream = new CdcStreamDefinition(
                Guid.NewGuid(),
                "pg-mirror-drift-contract",
                pgDs.Id,
                pubName,
                slotName,
                CdcTargetMode.RelationalTable,
                monitoredTables: $"public.{srcTable}",
                autoCreatePublicationAndSlot: true,
                maxBatchSize: 100,
                targetSinkId: pgDs.Id,
                targetSinkDestination: $"public.{tgtTable}",
                targetSinkKeyColumns: "order_id",
                initialSnapshotEnabled: true,
                targetDeletePolicy: CdcDeletePolicy.DeleteRow,
                schemaDriftPolicy: CdcSchemaDriftPolicy.PauseAndAlert);

            var service = new PgOutputCdcCaptureService(
                new EtlRelationalConnectionFactory(),
                new RecordingIntegrationEventPublisher(),
                null!,
                NullLogger<PgOutputCdcCaptureService>.Instance);

            var snap = await service.SnapshotAsync(pgDs, stream, pgDs, CancellationToken.None);
            snap.SnapshotRowCount.ShouldBe(1);
            snap.DeliveredCount.ShouldBe(1);
            stream.MarkSnapshotSucceeded(snap.ConsistentPointLsn, snap.SnapshotRowCount, snap.CompletedAtUtc);

            var initialEval = await service.EvaluateSchemaAsync(pgDs, stream, CancellationToken.None);
            stream.UpdateSchemaBaseline(initialEval.CurrentHash, initialEval.CurrentJson);

            await ExecuteNonQueryAsync(conn, $"""
                INSERT INTO public."{srcTable}" (order_id, customer_name, total_amount) VALUES (2, 'Bob', 250.00);
                UPDATE public."{srcTable}" SET customer_name = 'Alice-V2', total_amount = 120.00 WHERE order_id = 1;
                INSERT INTO public."{srcTable}" (order_id, customer_name, total_amount) VALUES (99, 'Gone', 1.00);
                DELETE FROM public."{srcTable}" WHERE order_id = 99;
                """);

            var delta = await service.CaptureBatchAsync(pgDs, stream, pgDs, CancellationToken.None);
            delta.CapturedEventCount.ShouldBe(4);
            stream.MarkCaptureSucceeded(delta.NewCheckpointLsn, delta.CapturedEventCount, delta.CapturedAtUtc);

            await using (var verifyCmd = new NpgsqlCommand(
                $"SELECT order_id, customer_name, total_amount FROM public.\"{tgtTable}\" ORDER BY order_id", conn))
            await using (var reader = await verifyCmd.ExecuteReaderAsync())
            {
                var mirrored = new List<(long Id, string Name, decimal Amount)>();
                while (await reader.ReadAsync())
                {
                    mirrored.Add((reader.GetInt64(0), reader.GetString(1), reader.GetDecimal(2)));
                }

                mirrored.ShouldBe(
                [
                    (1L, "Alice-V2", 120.00m),
                    (2L, "Bob", 250.00m),
                ]);
            }

            // Upstream DDL schema drift should trip PauseAndAlert before reading/advancing WAL
            await ExecuteNonQueryAsync(conn, $"ALTER TABLE public.\"{srcTable}\" ADD COLUMN loyalty_tier text;");
            var pfAfterDrift = await service.PreflightAsync(pgDs, stream, CancellationToken.None);
            pfAfterDrift.SchemaDriftDetected.ShouldBeTrue();
            pfAfterDrift.SchemaDriftSummary.ShouldNotBeNull().ShouldContain("+col loyalty_tier");

            var driftEx = await Should.ThrowAsync<Volo.Abp.BusinessException>(() =>
                service.CaptureBatchAsync(pgDs, stream, pgDs, CancellationToken.None));
            driftEx.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.CdcStreamSchemaDriftDetected);

            var acceptedEval = await service.EvaluateSchemaAsync(pgDs, stream, CancellationToken.None);
            stream.UpdateSchemaBaseline(acceptedEval.CurrentHash, acceptedEval.CurrentJson);
            var afterAccept = await service.CaptureBatchAsync(pgDs, stream, pgDs, CancellationToken.None);
            afterAccept.CapturedEventCount.ShouldBe(0);
        }
        finally
        {
            await ExecuteNonQueryAsync(conn, $"""
                SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_replication_slots WHERE slot_name = '{slotName}')
                    THEN pg_drop_replication_slot('{slotName}') END;
                DROP PUBLICATION IF EXISTS "{pubName}";
                DROP TABLE IF EXISTS public."{srcTable}";
                DROP TABLE IF EXISTS public."{tgtTable}";
                """);
        }
    }

    [PostgreSqlLogicalReplicationFact]
    public async Task CaptureBatchAsync_With_FieldMappings_And_SoftDelete_Should_Remap_And_Resurrect_In_PostgreSql()
    {
        var connStr = Environment.GetEnvironmentVariable(ConnectionEnvVar)
                      ?? Environment.GetEnvironmentVariable("SWAPMID_POSTGRES_TEST_CONNECTION");
        connStr.ShouldNotBeNullOrWhiteSpace("Missing PostgreSQL logical replication connection string.");

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var srcTable = $"cdc_src_sd_{suffix}";
        var tgtTable = $"cdc_tgt_sd_{suffix}";
        var pubName = $"pub_sd_{suffix}";
        var slotName = $"slot_sd_{suffix}";

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        // Source uses REPLICA IDENTITY DEFAULT to verify out-of-batch SoftDelete works with key-only DELETE WAL tuples.
        await ExecuteNonQueryAsync(conn, $"""
            CREATE TABLE public."{srcTable}" (
                src_order_id bigint PRIMARY KEY,
                src_customer text NOT NULL,
                src_amount numeric(14,2) NOT NULL,
                ignored_note text NULL
            );
            INSERT INTO public."{srcTable}" (src_order_id, src_customer, src_amount, ignored_note)
            VALUES (1001, 'SeedOne', 100.00, 'drop-me'), (1002, 'SeedTwo', 200.00, 'drop-me');

            CREATE TABLE public."{tgtTable}" (
                order_id bigint PRIMARY KEY,
                customer_code text NOT NULL,
                total_amount numeric(14,2) NOT NULL,
                is_deleted boolean NOT NULL DEFAULT false,
                deleted_at_utc timestamptz NULL
            );
            """);

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connStr);
            var dsJson = $$"""{"provider":"postgresql","host":"{{builder.Host}}","port":{{builder.Port}},"database":"{{builder.Database}}","username":"{{builder.Username}}","password":"{{builder.Password}}","encrypt":false,"trustServerCertificate":true}""";
            var pgDs = new DataSource(Guid.NewGuid(), "cdc-sd-pg", DataSourceType.RelationalDatabase, dsJson);

            const string mappingsJson =
                """[{"sourceColumn":"src_order_id","targetColumn":"order_id"},{"sourceColumn":"src_customer","targetColumn":"customer_code"},{"sourceColumn":"src_amount","targetColumn":"total_amount"}]""";

            var stream = new CdcStreamDefinition(
                Guid.NewGuid(),
                "pg-mirror-softdelete-contract",
                pgDs.Id,
                pubName,
                slotName,
                CdcTargetMode.RelationalTable,
                monitoredTables: $"public.{srcTable}",
                autoCreatePublicationAndSlot: true,
                maxBatchSize: 100,
                targetSinkId: pgDs.Id,
                targetSinkDestination: $"public.{tgtTable}",
                targetSinkKeyColumns: "order_id",
                initialSnapshotEnabled: true,
                targetDeletePolicy: CdcDeletePolicy.SoftDelete,
                schemaDriftPolicy: CdcSchemaDriftPolicy.Ignore,
                targetFieldMappingsJson: mappingsJson,
                softDeleteColumn: "is_deleted",
                softDeleteTimestampColumn: "deleted_at_utc");

            var connFactory = new EtlRelationalConnectionFactory();
            var preflightSvc = new CdcRelationalMirrorPreflightService(connFactory);
            var compat = await preflightSvc.EvaluateStreamAsync(pgDs, pgDs, stream, CancellationToken.None);
            compat.Compatible.ShouldBeTrue(string.Join("; ", compat.Issues.Select(i => i.Message)));

            var service = new PgOutputCdcCaptureService(
                connFactory,
                new RecordingIntegrationEventPublisher(),
                null!,
                NullLogger<PgOutputCdcCaptureService>.Instance);

            var snap = await service.SnapshotAsync(pgDs, stream, pgDs, CancellationToken.None);
            snap.SnapshotRowCount.ShouldBe(2);
            stream.MarkSnapshotSucceeded(snap.ConsistentPointLsn, snap.SnapshotRowCount, snap.CompletedAtUtc);

            // Batch 1: delete 1001, insert 1003, update 1002
            await ExecuteNonQueryAsync(conn, $"""
                DELETE FROM public."{srcTable}" WHERE src_order_id = 1001;
                INSERT INTO public."{srcTable}" (src_order_id, src_customer, src_amount, ignored_note) VALUES (1003, 'CreatedThree', 300.50, 'x');
                UPDATE public."{srcTable}" SET src_amount = 222.25 WHERE src_order_id = 1002;
                """);

            var delta1 = await service.CaptureBatchAsync(pgDs, stream, pgDs, CancellationToken.None);
            delta1.CapturedEventCount.ShouldBe(3);
            stream.MarkCaptureSucceeded(delta1.NewCheckpointLsn, delta1.CapturedEventCount, delta1.CapturedAtUtc);

            // Batch 2: resurrect 1001 via INSERT
            await ExecuteNonQueryAsync(conn, $"""
                INSERT INTO public."{srcTable}" (src_order_id, src_customer, src_amount, ignored_note) VALUES (1001, 'ResurrectedOne', 150.75, 'z');
                DELETE FROM public."{srcTable}" WHERE src_order_id = 1003;
                """);

            var delta2 = await service.CaptureBatchAsync(pgDs, stream, pgDs, CancellationToken.None);
            delta2.CapturedEventCount.ShouldBe(2);

            await using var verifyCmd = new NpgsqlCommand(
                $"SELECT order_id, customer_code, total_amount, is_deleted, deleted_at_utc IS NOT NULL FROM public.\"{tgtTable}\" ORDER BY order_id",
                conn);
            await using var reader = await verifyCmd.ExecuteReaderAsync();
            var rows = new List<(long Id, string Customer, decimal Amount, bool Deleted, bool HasDeletedAt)>();
            while (await reader.ReadAsync())
            {
                rows.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetDecimal(2),
                    reader.GetBoolean(3),
                    reader.GetBoolean(4)));
            }

            rows.ShouldBe(
            [
                (1001L, "ResurrectedOne", 150.75m, false, false),
                (1002L, "SeedTwo", 222.25m, false, false),
                (1003L, "CreatedThree", 300.50m, true, true),
            ]);
        }
        finally
        {
            await ExecuteNonQueryAsync(conn, $"""
                SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_replication_slots WHERE slot_name = '{slotName}')
                    THEN pg_drop_replication_slot('{slotName}') END;
                DROP PUBLICATION IF EXISTS "{pubName}";
                DROP TABLE IF EXISTS public."{srcTable}";
                DROP TABLE IF EXISTS public."{tgtTable}";
                """);
        }
    }

    private static async Task ExecuteNonQueryAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<IntegrationEventEnvelope> Envelopes { get; } = [];

        public Task<bool> PublishIfTopicActiveAsync(
            IntegrationEventEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return Task.FromResult(true);
        }
    }
}
