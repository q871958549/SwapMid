using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Volo.Abp;

namespace SwapMid.DataIntegration;

public sealed partial class SqlServerCdcCaptureService
{
    private sealed record SqlTableMetadata(
        List<string> PrimaryKeys,
        List<string> AllColumns,
        bool Tracked,
        long MinValidVersion);

    private sealed record SqlServerCtState(
        string? Version,
        bool DatabaseCtEnabled,
        long? CurrentVersion,
        int InvalidTables,
        bool CheckpointExpired,
        List<string> Diagnostics,
        Dictionary<string, SqlTableMetadata> Tables);

    private sealed record VersionedChange(long Version, int Sequence, PgOutputCapturedChange Change);

    private static async Task<SqlServerCtState> EnsureReadyAsync(
        SqlConnection conn,
        IReadOnlyList<MonitoredTableSpec> tables,
        string? checkpoint,
        bool checkExpired,
        CancellationToken cancellationToken)
    {
        var state = await InspectServerAsync(conn, tables, checkpoint, cancellationToken);
        if (!state.DatabaseCtEnabled || state.InvalidTables > 0 || (checkExpired && state.CheckpointExpired))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                .WithData("Reason", string.Join("；", state.Diagnostics));
        }

        return state;
    }

    private static async Task<SqlServerCtState> InspectServerAsync(
        SqlConnection conn,
        IReadOnlyList<MonitoredTableSpec> tables,
        string? checkpoint,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        string? versionText = null;
        await using (var vCmd = new SqlCommand("SELECT @@VERSION", conn))
        {
            versionText = Convert.ToString(await vCmd.ExecuteScalarAsync(cancellationToken));
            if (versionText != null && versionText.IndexOf('\n') is int nl && nl > 0)
            {
                versionText = versionText[..nl].Trim();
            }
        }

        bool dbCtEnabled;
        await using (var dbCmd = new SqlCommand(
            "SELECT COUNT(1) FROM sys.change_tracking_databases WHERE database_id = DB_ID()",
            conn))
        {
            dbCtEnabled = Convert.ToInt32(await dbCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
        }

        long? currentVersion = null;
        if (!dbCtEnabled)
        {
            diagnostics.Add("当前数据库未启用 Change Tracking（请执行 ALTER DATABASE ... SET CHANGE_TRACKING = ON）。");
        }
        else
        {
            await using var verCmd = new SqlCommand("SELECT CHANGE_TRACKING_CURRENT_VERSION()", conn);
            var raw = await verCmd.ExecuteScalarAsync(cancellationToken);
            currentVersion = raw is null or DBNull ? null : Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        }

        var hasStored = long.TryParse(checkpoint, NumberStyles.Integer, CultureInfo.InvariantCulture, out var storedVersion);
        var invalidTables = 0;
        var checkpointExpired = false;
        var tableMetaMap = new Dictionary<string, SqlTableMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var meta = await LoadTableMetadataAsync(conn, table.Schema, table.Table, cancellationToken);
            tableMetaMap[table.FullName] = meta;
            if (meta.AllColumns.Count == 0)
            {
                invalidTables++;
                diagnostics.Add($"源表 {table.FullName} 不存在。");
                continue;
            }

            if (!meta.Tracked)
            {
                invalidTables++;
                diagnostics.Add($"表 {table.FullName} 未启用 Change Tracking（ALTER TABLE ... ENABLE CHANGE_TRACKING）。");
            }

            if (meta.PrimaryKeys.Count == 0)
            {
                invalidTables++;
                diagnostics.Add($"表 {table.FullName} 缺少主键（SQL Server Change Tracking 要求目标表必须声明 PRIMARY KEY）。");
            }

            if (hasStored && meta.Tracked && storedVersion < meta.MinValidVersion)
            {
                checkpointExpired = true;
                diagnostics.Add(
                    $"表 {table.FullName} 本地检查点版本 {storedVersion} 已早于最小有效保留版本 {meta.MinValidVersion}，请执行全量快照重建基线。");
            }
        }

        return new SqlServerCtState(versionText, dbCtEnabled, currentVersion, invalidTables, checkpointExpired, diagnostics, tableMetaMap);
    }

    private static async Task<SqlTableMetadata> LoadTableMetadataAsync(
        SqlConnection conn,
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        var allColumns = new List<string>();
        await using (var colCmd = new SqlCommand("""
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION
            """, conn))
        {
            colCmd.Parameters.AddWithValue("@schema", schema);
            colCmd.Parameters.AddWithValue("@table", table);
            await using var r = await colCmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                allColumns.Add(r.GetString(0));
            }
        }

        var pks = new List<string>();
        await using (var pkCmd = new SqlCommand("""
            SELECT c.name
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE i.is_primary_key = 1 AND s.name = @schema AND t.name = @table
            ORDER BY ic.key_ordinal
            """, conn))
        {
            pkCmd.Parameters.AddWithValue("@schema", schema);
            pkCmd.Parameters.AddWithValue("@table", table);
            await using var r = await pkCmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                pks.Add(r.GetString(0));
            }
        }

        bool tracked;
        long minValid = 0;
        await using (var ctCmd = new SqlCommand("""
            SELECT CASE WHEN ct.object_id IS NOT NULL THEN 1 ELSE 0 END AS is_tracked,
                   ISNULL(CHANGE_TRACKING_MIN_VALID_VERSION(t.object_id), 0) AS min_valid
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            LEFT JOIN sys.change_tracking_tables ct ON ct.object_id = t.object_id
            WHERE s.name = @schema AND t.name = @table
            """, conn))
        {
            ctCmd.Parameters.AddWithValue("@schema", schema);
            ctCmd.Parameters.AddWithValue("@table", table);
            await using var r = await ctCmd.ExecuteReaderAsync(cancellationToken);
            tracked = await r.ReadAsync(cancellationToken) && r.GetInt32(0) == 1;
            minValid = tracked && !r.IsDBNull(1) ? Convert.ToInt64(r.GetValue(1), CultureInfo.InvariantCulture) : 0L;
        }

        return new SqlTableMetadata(pks, allColumns, tracked, minValid);
    }

    private static async Task<CdcSchemaDriftEvaluation> EvaluateSchemaCoreAsync(
        SqlConnection conn,
        IReadOnlyList<MonitoredTableSpec> tables,
        string? baselineJson,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<CdcCapturedTableSchema>(tables.Count);
        foreach (var table in tables)
        {
            var cols = new List<CdcCapturedTableColumn>();
            await using var cmd = new SqlCommand("""
                SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                ORDER BY ORDINAL_POSITION
                """, conn);
            cmd.Parameters.AddWithValue("@schema", table.Schema);
            cmd.Parameters.AddWithValue("@table", table.Table);
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                cols.Add(new CdcCapturedTableColumn(
                    r.GetString(0),
                    r.GetString(1),
                    string.Equals(r.GetString(2), "YES", StringComparison.OrdinalIgnoreCase)));
            }

            if (cols.Count > 0)
            {
                snapshots.Add(new CdcCapturedTableSchema(table.Schema, table.Table, cols));
            }
        }

        return CdcSchemaDriftEvaluator.Compare(baselineJson, snapshots);
    }

    private static async Task<bool> IsSnapshotIsolationEnabledAsync(
        SqlConnection conn,
        CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(
            "SELECT snapshot_isolation_state FROM sys.databases WHERE database_id = DB_ID()",
            conn);
        var state = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return state is 1 or 3;
    }

    private static async Task<List<VersionedChange>> ReadChangesAsync(
        SqlConnection conn,
        IReadOnlyList<MonitoredTableSpec> tables,
        Dictionary<string, SqlTableMetadata> metadata,
        long sinceVersion,
        long currentVersion,
        int maxBatchSize,
        CancellationToken cancellationToken)
    {
        var results = new List<VersionedChange>();
        var sequence = 0;
        var limit = maxBatchSize + 1;

        foreach (var table in tables)
        {
            var meta = metadata[table.FullName];
            var pkSet = new HashSet<string>(meta.PrimaryKeys, StringComparer.OrdinalIgnoreCase);
            var nonPkCols = meta.AllColumns.Where(c => !pkSet.Contains(c)).ToList();

            var pkSelect = string.Join(", ", meta.PrimaryKeys.Select(pk => $"CT.{QuotePart(pk)} AS {QuotePart(pk)}"));
            var nonPkSelect = nonPkCols.Count > 0
                ? ", " + string.Join(", ", nonPkCols.Select(c => $"T.{QuotePart(c)} AS {QuotePart(c)}"))
                : string.Empty;
            var joinClause = string.Join(" AND ", meta.PrimaryKeys.Select(pk => $"CT.{QuotePart(pk)} = T.{QuotePart(pk)}"));
            var orderClause = string.Join(", ", meta.PrimaryKeys.Select(pk => $"CT.{QuotePart(pk)} ASC"));
            var fullTable = $"{QuotePart(table.Schema)}.{QuotePart(table.Table)}";

            var sql = $"""
                SELECT TOP (@limit)
                    CT.SYS_CHANGE_VERSION,
                    CT.SYS_CHANGE_OPERATION,
                    {pkSelect}{nonPkSelect}
                FROM CHANGETABLE(CHANGES {fullTable}, @sinceVer) AS CT
                LEFT JOIN {fullTable} AS T ON {joinClause}
                WHERE CT.SYS_CHANGE_VERSION <= @currVer
                ORDER BY CT.SYS_CHANGE_VERSION ASC, {orderClause}
                """;

            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = CommandTimeoutSeconds;
            cmd.Parameters.AddWithValue("@limit", limit);
            cmd.Parameters.AddWithValue("@sinceVer", sinceVersion);
            cmd.Parameters.AddWithValue("@currVer", currentVersion);

            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var ver = Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture);
                var opChar = r.GetString(1).Trim().ToUpperInvariant();
                var op = opChar switch
                {
                    "I" => "insert",
                    "U" => "update",
                    "D" => "delete",
                    _ => "update",
                };

                var verStr = ver.ToString(CultureInfo.InvariantCulture);
                if (op == "delete")
                {
                    var oldDict = new Dictionary<string, object?>(meta.PrimaryKeys.Count, StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < meta.PrimaryKeys.Count; i++)
                    {
                        var val = await r.IsDBNullAsync(2 + i, cancellationToken) ? null : r.GetValue(2 + i);
                        oldDict[meta.PrimaryKeys[i]] = val;
                    }

                    results.Add(new VersionedChange(
                        ver,
                        sequence++,
                        new PgOutputCapturedChange(table.Schema, table.Table, op, verStr, DateTime.UtcNow, null, oldDict)));
                }
                else
                {
                    var dataDict = new Dictionary<string, object?>(meta.AllColumns.Count, StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < meta.PrimaryKeys.Count; i++)
                    {
                        var val = await r.IsDBNullAsync(2 + i, cancellationToken) ? null : r.GetValue(2 + i);
                        dataDict[meta.PrimaryKeys[i]] = val;
                    }

                    for (var i = 0; i < nonPkCols.Count; i++)
                    {
                        var ordinal = 2 + meta.PrimaryKeys.Count + i;
                        var val = await r.IsDBNullAsync(ordinal, cancellationToken) ? null : r.GetValue(ordinal);
                        dataDict[nonPkCols[i]] = val;
                    }

                    results.Add(new VersionedChange(
                        ver,
                        sequence++,
                        new PgOutputCapturedChange(table.Schema, table.Table, op, verStr, DateTime.UtcNow, dataDict, null)));
                }
            }
        }

        return results;
    }
}
