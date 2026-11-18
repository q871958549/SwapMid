using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace SwapMid.DataIntegration;

public class CdcRelationalMirrorSqliteContractTests
{
    [Fact]
    public async Task DeliverAsync_Should_Materialize_Upsert_And_Delete_Into_Sqlite_Target_Table()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"swapmid-cdc-mirror-{Guid.NewGuid():N}.db");
        var connString = $"Data Source={dbPath};Pooling=False";
        try
        {
            await using (var initConn = new SqliteConnection(connString))
            {
                await initConn.OpenAsync();
                await using var cmd = initConn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE orders_mirror (
                        order_id INTEGER PRIMARY KEY,
                        customer_code TEXT NOT NULL,
                        amount REAL NOT NULL,
                        ordered_date TEXT NULL
                    );
                    INSERT INTO orders_mirror (order_id, customer_code, amount, ordered_date) VALUES (99, 'OLD', 5.0, '2026-01-01');
                    """;
                await cmd.ExecuteNonQueryAsync();
            }

            var targetSource = new DataSource(
                Guid.NewGuid(),
                "sqlite-mirror-target",
                DataSourceType.RelationalDatabase,
                """{"provider":"sqlite","host":"localhost","port":0,"database":"mirror"}""");

            var stream = new CdcStreamDefinition(
                Guid.NewGuid(),
                "orders-mirror-stream",
                Guid.NewGuid(),
                "pub_orders",
                "slot_orders",
                CdcTargetMode.RelationalTable,
                monitoredTables: "public.orders",
                targetSinkId: targetSource.Id,
                targetSinkDestination: "orders_mirror",
                targetSinkKeyColumns: "order_id",
                targetDeletePolicy: CdcDeletePolicy.DeleteRow);

            var factory = new FixedSqliteConnectionFactory(connString);
            var now = DateTime.UtcNow;
            IReadOnlyList<PgOutputCapturedChange> batch1 =
            [
                new("public", "orders", "snapshot", "0/20", now, new Dictionary<string, object?> { ["order_id"] = 1L, ["customer_code"] = "C1", ["amount"] = 100.5d, ["ordered_date"] = "2026-11-17" }, null),
                new("public", "orders", "insert", "0/21", now, new Dictionary<string, object?> { ["order_id"] = 2L, ["customer_code"] = "C2", ["amount"] = 200d, ["ordered_date"] = "2026-11-17" }, null),
                new("public", "orders", "update", "0/22", now, new Dictionary<string, object?> { ["order_id"] = 1L, ["customer_code"] = "C1-UPD", ["amount"] = 150.25d, ["ordered_date"] = "2026-11-17" }, null),
                new("public", "orders", "delete", "0/23", now, null, new Dictionary<string, object?> { ["order_id"] = 99L }),
            ];

            var applied = await CdcRelationalMirrorDeliveryAdapter.DeliverAsync(
                stream,
                targetSource,
                batch1,
                factory,
                NullLogger.Instance,
                CancellationToken.None);

            applied.ShouldBe(3);

            await using var checkConn = new SqliteConnection(connString);
            await checkConn.OpenAsync();
            await using var readCmd = checkConn.CreateCommand();
            readCmd.CommandText = "SELECT order_id, customer_code, amount FROM orders_mirror ORDER BY order_id";
            await using var reader = await readCmd.ExecuteReaderAsync();

            var rows = new List<(long Id, string Customer, double Amount)>();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetDouble(2)));
            }

            rows.Count.ShouldBe(2);
            rows[0].ShouldBe((1L, "C1-UPD", 150.25d));
            rows[1].ShouldBe((2L, "C2", 200d));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task DeliverAsync_With_FieldMappings_And_SoftDelete_Should_Update_Tombstone_And_Resurrect_On_Reinsert()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"swapmid-cdc-softdel-{Guid.NewGuid():N}.db");
        var connString = $"Data Source={dbPath};Pooling=False";
        try
        {
            await using (var initConn = new SqliteConnection(connString))
            {
                await initConn.OpenAsync();
                await using var cmd = initConn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE orders_soft_mirror (
                        order_id INTEGER PRIMARY KEY,
                        client_ref TEXT NOT NULL,
                        net_amount REAL NOT NULL,
                        is_deleted INTEGER NOT NULL DEFAULT 0,
                        deleted_at_utc TEXT NULL
                    );
                    """;
                await cmd.ExecuteNonQueryAsync();
            }

            var targetSource = new DataSource(
                Guid.NewGuid(),
                "sqlite-soft-target",
                DataSourceType.RelationalDatabase,
                """{"provider":"sqlite","host":"localhost","port":0,"database":"mirror"}""");

            const string mappingsJson =
                """[{"sourceColumn":"src_id","targetColumn":"order_id"},{"sourceColumn":"src_customer","targetColumn":"client_ref"},{"sourceColumn":"src_total","targetColumn":"net_amount"}]""";

            var stream = new CdcStreamDefinition(
                Guid.NewGuid(),
                "orders-soft-mirror-stream",
                Guid.NewGuid(),
                "pub_orders_soft",
                "slot_orders_soft",
                CdcTargetMode.RelationalTable,
                monitoredTables: "public.orders",
                targetSinkId: targetSource.Id,
                targetSinkDestination: "orders_soft_mirror",
                targetSinkKeyColumns: "order_id",
                targetDeletePolicy: CdcDeletePolicy.SoftDelete,
                targetFieldMappingsJson: mappingsJson,
                softDeleteColumn: "is_deleted",
                softDeleteTimestampColumn: "deleted_at_utc");

            var factory = new FixedSqliteConnectionFactory(connString);
            var t0 = new DateTime(2026, 11, 18, 8, 0, 0, DateTimeKind.Utc);
            IReadOnlyList<PgOutputCapturedChange> batch1 =
            [
                new("public", "orders", "snapshot", "0/30", t0, new Dictionary<string, object?> { ["src_id"] = 100L, ["src_customer"] = "CUST-A", ["src_total"] = 88.5d, ["extra_unmapped"] = "drop" }, null),
                new("public", "orders", "insert", "0/31", t0.AddSeconds(1), new Dictionary<string, object?> { ["src_id"] = 101L, ["src_customer"] = "CUST-B", ["src_total"] = 120.0d, ["extra_unmapped"] = "drop" }, null),
                new("public", "orders", "insert", "0/32", t0.AddSeconds(2), new Dictionary<string, object?> { ["src_id"] = 102L, ["src_customer"] = "CUST-C", ["src_total"] = 55.0d, ["extra_unmapped"] = "drop" }, null),
                new("public", "orders", "delete", "0/33", t0.AddSeconds(3), null, new Dictionary<string, object?> { ["src_id"] = 102L }),
            ];

            await CdcRelationalMirrorDeliveryAdapter.DeliverAsync(
                stream, targetSource, batch1, factory, NullLogger.Instance, CancellationToken.None);

            // Batch 2: out-of-batch DELETE (key-only OldData as emitted under REPLICA IDENTITY DEFAULT) on 101, plus resurrection INSERT on 102
            var t1 = t0.AddMinutes(5);
            IReadOnlyList<PgOutputCapturedChange> batch2 =
            [
                new("public", "orders", "delete", "0/40", t1, null, new Dictionary<string, object?> { ["src_id"] = 101L }),
                new("public", "orders", "insert", "0/41", t1.AddSeconds(2), new Dictionary<string, object?> { ["src_id"] = 102L, ["src_customer"] = "CUST-C-REVIVED", ["src_total"] = 99.75d }, null),
            ];

            await CdcRelationalMirrorDeliveryAdapter.DeliverAsync(
                stream, targetSource, batch2, factory, NullLogger.Instance, CancellationToken.None);

            await using var checkConn = new SqliteConnection(connString);
            await checkConn.OpenAsync();
            await using var readCmd = checkConn.CreateCommand();
            readCmd.CommandText = "SELECT order_id, client_ref, net_amount, is_deleted, deleted_at_utc FROM orders_soft_mirror ORDER BY order_id";
            await using var reader = await readCmd.ExecuteReaderAsync();

            var rows = new List<(long Id, string ClientRef, double Amount, long Deleted, string? DeletedAt)>();
            while (await reader.ReadAsync())
            {
                rows.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetDouble(2),
                    reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }

            rows.Count.ShouldBe(3);
            rows[0].Id.ShouldBe(100L);
            rows[0].ClientRef.ShouldBe("CUST-A");
            rows[0].Deleted.ShouldBe(0L);
            rows[0].DeletedAt.ShouldBeNull();

            rows[1].Id.ShouldBe(101L);
            rows[1].ClientRef.ShouldBe("CUST-B");
            rows[1].Deleted.ShouldBe(1L);
            rows[1].DeletedAt.ShouldNotBeNullOrWhiteSpace();

            rows[2].Id.ShouldBe(102L);
            rows[2].ClientRef.ShouldBe("CUST-C-REVIVED");
            rows[2].Amount.ShouldBe(99.75d);
            rows[2].Deleted.ShouldBe(0L);
            rows[2].DeletedAt.ShouldBeNull();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private sealed class FixedSqliteConnectionFactory(string connectionString) : IEtlRelationalConnectionFactory
    {
        public DbConnection Create(string configJson, int connectTimeoutSeconds)
            => new SqliteConnection(connectionString);
    }
}
