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

    private sealed class FixedSqliteConnectionFactory(string connectionString) : IEtlRelationalConnectionFactory
    {
        public DbConnection Create(string configJson, int connectTimeoutSeconds)
            => new SqliteConnection(connectionString);
    }
}
