using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Volo.Abp;

namespace SwapMid.DataIntegration;

internal sealed record PgServerReplicationState(
    string WalLevel,
    string Version,
    string? CurrentWalLsn,
    bool PublicationExists,
    bool SlotExists,
    bool SlotActive,
    string? SlotPlugin,
    string? ConfirmedFlushLsn,
    long? WalLagBytes,
    List<CdcTableReplicaIdentityDto> TableReplicaIdentities);

/// <summary>
/// 读取 PostgreSQL 逻辑复制服务端元状态、监听表 REPLICA IDENTITY 与 WAL 滞留字节，并安全释放闲置复制槽。
/// </summary>
internal static class PgOutputReplicationMetadataReader
{
    internal static async Task<PgServerReplicationState> InspectServerStateAsync(
        NpgsqlConnection conn,
        string publicationName,
        string slotName,
        IReadOnlyList<MonitoredTableSpec> monitoredTables,
        CancellationToken cancellationToken)
    {
        string walLevel;
        string version;
        string? currentWalLsn;

        await using (var cmd = new NpgsqlCommand("SHOW wal_level", conn))
        {
            walLevel = Convert.ToString(await cmd.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
        }

        await using (var cmd = new NpgsqlCommand("SELECT version(), pg_current_wal_lsn()::text", conn))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            version = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            currentWalLsn = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        bool pubExists;
        await using (var pubCmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_publication WHERE pubname = @pub)", conn))
        {
            pubCmd.Parameters.AddWithValue("pub", publicationName);
            pubExists = Convert.ToBoolean(await pubCmd.ExecuteScalarAsync(cancellationToken));
        }

        var slotExists = false;
        var slotActive = false;
        string? slotPlugin = null;
        string? confirmedFlushLsn = null;
        long? walLagBytes = null;

        await using (var slotCmd = new NpgsqlCommand(
            """
            SELECT plugin,
                   active,
                   confirmed_flush_lsn::text,
                   CASE
                       WHEN restart_lsn IS NULL THEN NULL
                       ELSE GREATEST(pg_wal_lsn_diff(pg_current_wal_lsn(), restart_lsn)::bigint, 0)
                   END AS wal_lag_bytes
            FROM pg_replication_slots
            WHERE slot_name = @slot
            """,
            conn))
        {
            slotCmd.Parameters.AddWithValue("slot", slotName);
            await using var reader = await slotCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                slotExists = true;
                slotPlugin = reader.IsDBNull(0) ? null : reader.GetString(0);
                slotActive = !reader.IsDBNull(1) && reader.GetBoolean(1);
                confirmedFlushLsn = reader.IsDBNull(2) ? null : reader.GetString(2);
                walLagBytes = reader.IsDBNull(3) ? null : reader.GetInt64(3);
            }
        }

        var identities = await QueryTableReplicaIdentitiesAsync(
            conn,
            publicationName,
            pubExists,
            monitoredTables,
            cancellationToken);

        return new PgServerReplicationState(
            walLevel,
            version,
            currentWalLsn,
            pubExists,
            slotExists,
            slotActive,
            slotPlugin,
            confirmedFlushLsn,
            walLagBytes,
            identities);
    }

    internal static async Task<List<CdcTableReplicaIdentityDto>> QueryTableReplicaIdentitiesAsync(
        NpgsqlConnection conn,
        string publicationName,
        bool publicationExists,
        IReadOnlyList<MonitoredTableSpec> monitoredTables,
        CancellationToken cancellationToken)
    {
        var results = new List<CdcTableReplicaIdentityDto>();
        if (monitoredTables.Count > 0)
        {
            var fullNames = new string[monitoredTables.Count];
            for (var i = 0; i < monitoredTables.Count; i++)
            {
                fullNames[i] = monitoredTables[i].FullName.ToLowerInvariant();
            }

            await using var cmd = new NpgsqlCommand(
                """
                SELECT n.nspname, c.relname, c.relreplident::text
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE c.relkind IN ('r', 'p')
                  AND LOWER(n.nspname || '.' || c.relname) = ANY(@tables)
                ORDER BY n.nspname, c.relname
                """,
                conn);
            cmd.Parameters.AddWithValue("tables", fullNames);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new CdcTableReplicaIdentityDto
                {
                    Schema = reader.GetString(0),
                    Table = reader.GetString(1),
                    ReplicaIdentity = CdcProtocolHelpers.MapReplicaIdentityCode(
                        reader.IsDBNull(2) ? null : reader.GetString(2)),
                });
            }

            return results;
        }

        if (!publicationExists)
        {
            return results;
        }

        await using (var pubTablesCmd = new NpgsqlCommand(
            """
            SELECT n.nspname, c.relname, c.relreplident::text
            FROM pg_publication_tables pt
            JOIN pg_namespace n ON n.nspname = pt.schemaname
            JOIN pg_class c ON c.relnamespace = n.oid AND c.relname = pt.tablename
            WHERE pt.pubname = @pub
              AND c.relkind IN ('r', 'p')
            ORDER BY n.nspname, c.relname
            LIMIT @maxCount
            """,
            conn))
        {
            pubTablesCmd.Parameters.AddWithValue("pub", publicationName);
            pubTablesCmd.Parameters.AddWithValue("maxCount", CdcStreamConsts.MaxMonitoredTableCount);
            await using var reader = await pubTablesCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new CdcTableReplicaIdentityDto
                {
                    Schema = reader.GetString(0),
                    Table = reader.GetString(1),
                    ReplicaIdentity = CdcProtocolHelpers.MapReplicaIdentityCode(
                        reader.IsDBNull(2) ? null : reader.GetString(2)),
                });
            }
        }

        return results;
    }

    internal static async Task<(bool Dropped, string Message)> DropReplicationSlotAsync(
        NpgsqlConnection conn,
        string slotName,
        CancellationToken cancellationToken)
    {
        bool? isActive = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await using var checkCmd = new NpgsqlCommand(
                "SELECT active FROM pg_replication_slots WHERE slot_name = @slotName",
                conn);
            checkCmd.Parameters.AddWithValue("slotName", slotName);
            var scalar = await checkCmd.ExecuteScalarAsync(cancellationToken);
            if (scalar is null || scalar is DBNull)
            {
                return (false, $"复制槽 \"{slotName}\" 已不存在或尚未创建，无需释放。");
            }

            isActive = Convert.ToBoolean(scalar);
            if (!isActive.Value)
            {
                break;
            }

            await Task.Delay(25, cancellationToken);
        }

        if (isActive == true)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamCaptureBusy)
                .WithData("Reason", $"复制槽 \"{slotName}\" 当前仍有活跃复制会话，请先暂停并等待拉取结束再重试。");
        }

        await using var dropCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*)::int FROM (
                SELECT pg_drop_replication_slot(slot_name)
                FROM pg_replication_slots
                WHERE slot_name = @slotName
                  AND NOT active
            ) t
            """,
            conn);
        dropCmd.Parameters.AddWithValue("slotName", slotName);
        var droppedCount = Convert.ToInt32(await dropCmd.ExecuteScalarAsync(cancellationToken));

        return droppedCount > 0
            ? (true, $"已成功释放 PostgreSQL 逻辑复制槽 \"{slotName}\"，WAL 保留已解除。")
            : (false, $"复制槽 \"{slotName}\" 已不存在或处于繁忙状态，未执行删除。");
    }

    internal static List<string> BuildPreflightDiagnostics(
        CdcStreamDefinition stream,
        string publicationName,
        string slotName,
        PgServerReplicationState state)
    {
        var diagnostics = new List<string>();
        if (!string.Equals(state.WalLevel, "logical", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add($"当前 wal_level 为 \"{state.WalLevel}\"，需配置为 \"logical\" 并重启 PostgreSQL（ALTER SYSTEM SET wal_level = logical）。");
        }

        if (!state.PublicationExists)
        {
            diagnostics.Add(stream.AutoCreatePublicationAndSlot
                ? $"发布 \"{publicationName}\" 尚未创建，将在首次捕获时自动创建。"
                : $"发布 \"{publicationName}\" 不存在，请先手动执行 CREATE PUBLICATION 或开启自动建发布。");
        }

        if (!state.SlotExists)
        {
            diagnostics.Add(stream.AutoCreatePublicationAndSlot
                ? $"逻辑复制槽 \"{slotName}\"（pgoutput）尚未创建，将在首次捕获时自动创建。"
                : $"逻辑复制槽 \"{slotName}\" 不存在，请先创建或开启自动建槽。");
        }
        else if (!string.Equals(state.SlotPlugin, "pgoutput", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add($"复制槽 \"{slotName}\" 当前使用插件 \"{state.SlotPlugin}\"，并非 pgoutput。");
        }

        if (state.WalLagBytes >= CdcStreamConsts.WalLagWarningBytes)
        {
            var lagMb = Math.Round(state.WalLagBytes.Value / (1024d * 1024d), 2);
            diagnostics.Add($"复制槽 \"{slotName}\" 当前保留 WAL 滞留 {lagMb} MB，建议检查轮询状态或释放闲置复制槽。");
        }

        foreach (var table in state.TableReplicaIdentities)
        {
            if (table.ReplicaIdentity is "default" or "nothing")
            {
                diagnostics.Add(
                    $"表 {table.Schema}.{table.Table} 当前 REPLICA IDENTITY={table.ReplicaIdentity}，UPDATE 事件不包含 old 旧值与 changedColumns；若需旧行 Diff 请执行 ALTER TABLE {table.Schema}.{table.Table} REPLICA IDENTITY FULL。");
            }
        }

        return diagnostics;
    }

    internal static async Task<List<CdcCapturedTableSchema>> InspectTableSchemasAsync(
        NpgsqlConnection conn,
        string publicationName,
        bool publicationExists,
        IReadOnlyList<MonitoredTableSpec> monitoredTables,
        CancellationToken cancellationToken)
    {
        var columnsByTable = new Dictionary<string, (string Schema, string Table, List<CdcCapturedTableColumn> Columns)>(
            StringComparer.OrdinalIgnoreCase);

        NpgsqlCommand cmd;
        if (monitoredTables.Count > 0)
        {
            var fullNames = new string[monitoredTables.Count];
            for (var i = 0; i < monitoredTables.Count; i++)
            {
                fullNames[i] = monitoredTables[i].FullName.ToLowerInvariant();
            }

            cmd = new NpgsqlCommand(
                """
                SELECT n.nspname,
                       c.relname,
                       a.attname,
                       format_type(a.atttypid, a.atttypmod),
                       NOT a.attnotnull
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE c.relkind IN ('r', 'p')
                  AND a.attnum > 0
                  AND NOT a.attisdropped
                  AND LOWER(n.nspname || '.' || c.relname) = ANY(@tables)
                ORDER BY n.nspname, c.relname, a.attnum
                """,
                conn);
            cmd.Parameters.AddWithValue("tables", fullNames);
        }
        else if (publicationExists)
        {
            cmd = new NpgsqlCommand(
                """
                SELECT pt.schemaname,
                       pt.tablename,
                       a.attname,
                       format_type(a.atttypid, a.atttypmod),
                       NOT a.attnotnull
                FROM pg_publication_tables pt
                JOIN pg_namespace n ON n.nspname = pt.schemaname
                JOIN pg_class c ON c.relnamespace = n.oid AND c.relname = pt.tablename
                JOIN pg_attribute a ON a.attrelid = c.oid
                WHERE pt.pubname = @pub
                  AND c.relkind IN ('r', 'p')
                  AND a.attnum > 0
                  AND NOT a.attisdropped
                ORDER BY pt.schemaname, pt.tablename, a.attnum
                """,
                conn);
            cmd.Parameters.AddWithValue("pub", publicationName);
        }
        else
        {
            return new List<CdcCapturedTableSchema>();
        }

        await using (cmd)
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                var colName = reader.GetString(2);
                var dataType = reader.GetString(3);
                var isNullable = reader.GetBoolean(4);
                var key = $"{schema}.{table}";
                if (!columnsByTable.TryGetValue(key, out var entry))
                {
                    entry = (schema, table, new List<CdcCapturedTableColumn>());
                    columnsByTable[key] = entry;
                }

                entry.Columns.Add(new CdcCapturedTableColumn(colName, dataType, isNullable));
            }
        }

        var result = new List<CdcCapturedTableSchema>(columnsByTable.Count);
        foreach (var entry in columnsByTable.Values)
        {
            result.Add(new CdcCapturedTableSchema(entry.Schema, entry.Table, entry.Columns));
        }

        return result;
    }
}
