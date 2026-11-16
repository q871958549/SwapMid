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
