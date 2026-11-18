using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Shouldly;
using SwapMid.SharedKernel.IntegrationEvents;
using Xunit;

namespace SwapMid.DataIntegration;

public sealed class MySqlContractFactAttribute : FactAttribute
{
    public const string ConnectionEnvVar = "SWAPMID_MYSQL_TEST_CONNECTION";

    public MySqlContractFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
        {
            Skip = $"Requires MySQL 8.x ROW binlog instance ({ConnectionEnvVar}).";
        }
    }
}

public sealed class MySqlBinlogCdcContractTests
{
    [MySqlContractFact]
    public async Task Snapshot_Capture_And_SchemaDrift_Should_Work_EndToEnd_On_MySql()
    {
        var connStr = Environment.GetEnvironmentVariable(MySqlContractFactAttribute.ConnectionEnvVar)!;
        var builder = new MySqlConnectionStringBuilder(connStr);
        var dbName = string.IsNullOrWhiteSpace(builder.Database) ? "test" : builder.Database;
        var table = $"cdc_mysql_{Guid.NewGuid():N}"[..24];

        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();

        await ExecuteNonQueryAsync(conn, $"""
            CREATE TABLE `{dbName}`.`{table}` (
                id BIGINT NOT NULL PRIMARY KEY,
                customer VARCHAR(64) NOT NULL,
                total_amount DECIMAL(12,2) NOT NULL
            ) ENGINE=InnoDB;
            INSERT INTO `{dbName}`.`{table}` (id, customer, total_amount) VALUES
                (101, 'Alice', 100.00),
                (102, 'Bob', 250.50);
            """);

        try
        {
            var source = new DataSource(
                Guid.NewGuid(),
                "mysql-binlog-src",
                DataSourceType.RelationalDatabase,
                $$"""{"provider":"mysql","host":"{{builder.Server}}","port":{{builder.Port}},"database":"{{dbName}}","username":"{{builder.UserID}}","password":"{{builder.Password}}","encrypt":false,"trustServerCertificate":true}""");

            var stream = new CdcStreamDefinition(
                Guid.NewGuid(),
                "mysql-contract-stream",
                source.Id,
                "mysql_binlog",
                "binlog_slot",
                CdcTargetMode.MessageBusTopic,
                targetTopicName: IntegrationTopicNames.CdcEvents,
                monitoredTables: $"{dbName}.{table}",
                autoCreatePublicationAndSlot: true,
                initialSnapshotEnabled: true,
                schemaDriftPolicy: CdcSchemaDriftPolicy.Warn,
                sourceType: CdcSourceType.MySqlBinlog);

            var publisher = new RecordingIntegrationEventPublisher();
            var service = new MySqlBinlogCdcCaptureService(
                new EtlRelationalConnectionFactory(),
                publisher,
                null!,
                Array.Empty<IEtlSinkWriter>(),
                NullLogger<MySqlBinlogCdcCaptureService>.Instance);

            var preflight = await service.PreflightAsync(source, stream, CancellationToken.None);
            preflight.IsReady.ShouldBeTrue(string.Join("; ", preflight.Diagnostics));
            preflight.SlotPlugin.ShouldBe("mysql_binlog");

            var schema = await service.EvaluateSchemaAsync(source, stream, CancellationToken.None);
            stream.UpdateSchemaBaseline(schema.CurrentHash, schema.CurrentJson);

            var snap = await service.SnapshotAsync(source, stream, null, CancellationToken.None);
            snap.SnapshotRowCount.ShouldBe(2);
            snap.ConsistentPointLsn.ShouldNotBeNullOrWhiteSpace();
            stream.MarkSnapshotSucceeded(snap.ConsistentPointLsn, snap.SnapshotRowCount, snap.CompletedAtUtc);

            await ExecuteNonQueryAsync(conn, $"""
                INSERT INTO `{dbName}`.`{table}` (id, customer, total_amount) VALUES (103, 'Charlie', 320.00);
                UPDATE `{dbName}`.`{table}` SET customer = 'AliceUpdated', total_amount = 115.25 WHERE id = 101;
                DELETE FROM `{dbName}`.`{table}` WHERE id = 102;
                """);

            var delta = await service.CaptureBatchAsync(source, stream, null, CancellationToken.None);
            delta.CapturedEventCount.ShouldBe(3);
            delta.NewCheckpointLsn.ShouldNotBe(snap.ConsistentPointLsn);
            publisher.Envelopes.Count.ShouldBe(5);
            publisher.Envelopes.Skip(2).Any(e => e.Subject.EndsWith(".insert")).ShouldBeTrue();
            publisher.Envelopes.Skip(2).Any(e => e.Subject.EndsWith(".update")).ShouldBeTrue();
            publisher.Envelopes.Skip(2).Any(e => e.Subject.EndsWith(".delete")).ShouldBeTrue();

            await ExecuteNonQueryAsync(conn, $"ALTER TABLE `{dbName}`.`{table}` ADD COLUMN vip_tag VARCHAR(32) NULL;");
            var drift = await service.EvaluateSchemaAsync(source, stream, CancellationToken.None);
            drift.HasDrift.ShouldBeTrue();
            drift.Summary.ShouldContain("vip_tag");
        }
        finally
        {
            await ExecuteNonQueryAsync(conn, $"DROP TABLE IF EXISTS `{dbName}`.`{table}`;");
        }
    }

    private static async Task ExecuteNonQueryAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
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
