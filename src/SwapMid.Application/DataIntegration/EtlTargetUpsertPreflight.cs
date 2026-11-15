using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace SwapMid.DataIntegration;

/// <summary>
/// Upsert 目标表前置与事务锁内二次契约核验：
/// 1. 源投影必须覆盖全部 <c>keyColumns</c>、可选 <c>watermarkColumn</c>，且至少包含一个非键更新列；
/// 2. 目标表在真实数据库驱动下不得挂载启用态 UPDATE trigger；
/// 3. 目标表 <c>keyColumns</c> 必须全部为 NOT NULL，且由精确匹配的 PRIMARY KEY 或非过滤 UNIQUE 索引承接。
/// </summary>
internal static class EtlTargetUpsertPreflight
{
    internal sealed record ResolvedUpsertPlan(
        IReadOnlyList<string> KeyColumns,
        IReadOnlyList<string> UpdateColumns,
        string? WatermarkColumn);

    internal static ResolvedUpsertPlan ResolveProjection(
        IReadOnlyList<string> sourceColumns,
        EtlUpsertSpec spec)
    {
        var sourceByName = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var col in sourceColumns)
        {
            if (!sourceByName.TryAdd(col, col))
            {
                throw EtlPipelineRunner.InvalidInput();
            }
        }

        var resolvedKeys = new List<string>(spec.KeyColumns.Count);
        var keySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in spec.KeyColumns)
        {
            if (!sourceByName.TryGetValue(key, out var actual) ||
                !keySet.Add(actual))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            resolvedKeys.Add(actual);
        }

        string? resolvedWatermark = null;
        if (!string.IsNullOrWhiteSpace(spec.WatermarkColumn))
        {
            if (!sourceByName.TryGetValue(spec.WatermarkColumn, out var actualWm) ||
                keySet.Contains(actualWm))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            resolvedWatermark = actualWm;
        }

        var updateColumns = sourceColumns
            .Where(col => !keySet.Contains(col))
            .ToArray();
        if (updateColumns.Length == 0)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return new ResolvedUpsertPlan(resolvedKeys, updateColumns, resolvedWatermark);
    }

    internal static async Task ValidateTargetAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string canonicalTargetTable,
        RelationalDataSourceConnection.Probe targetProbe,
        EtlUpsertSpec spec,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        if (connection is SqlConnection &&
            targetProbe.NormalizedProvider is "sqlserver" or "mssql")
        {
            await ValidateSqlServerTargetAsync(
                connection,
                transaction,
                canonicalTargetTable,
                spec.KeyColumns,
                commandTimeoutSeconds,
                ct);
            return;
        }

        if (connection is NpgsqlConnection &&
            targetProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql")
        {
            await ValidatePostgreSqlTargetAsync(
                connection,
                transaction,
                canonicalTargetTable,
                spec.KeyColumns,
                commandTimeoutSeconds,
                ct);
            return;
        }

        if (connection is MySqlConnection &&
            targetProbe.NormalizedProvider is "mysql" or "mariadb")
        {
            await ValidateMySqlTargetAsync(
                connection,
                transaction,
                canonicalTargetTable,
                spec.KeyColumns,
                commandTimeoutSeconds,
                ct);
        }
    }

    private static async Task ValidatePostgreSqlTargetAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string canonicalTargetTable,
        IReadOnlyList<string> keyColumns,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var (schema, table) = SplitSchemaAndTable(canonicalTargetTable, "public");

        const string updateTriggerCheckSql = """
            SELECT NOT EXISTS (
                SELECT 1
                FROM pg_catalog.pg_trigger AS tr
                INNER JOIN pg_catalog.pg_class AS c ON c.oid = tr.tgrelid
                INNER JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema
                  AND c.relname = @table
                  AND NOT tr.tgisinternal
                  AND tr.tgenabled <> 'D'
                  AND (tr.tgtype & 16) <> 0
            )
            """;
        var noUpdateTriggers = await connection.QuerySingleAsync<bool>(
            new CommandDefinition(
                updateTriggerCheckSql,
                new { schema, table },
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        if (!noUpdateTriggers)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        const string uniqueIndexColumnsSql = """
            SELECT
                i.indexrelid::bigint AS IndexId,
                a.attname AS ColumnName,
                a.attnotnull AS IsNotNull
            FROM pg_catalog.pg_index AS i
            INNER JOIN pg_catalog.pg_class AS c ON c.oid = i.indrelid
            INNER JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            INNER JOIN LATERAL unnest(i.indkey[0:i.indnkeyatts - 1]) WITH ORDINALITY AS k(attnum, ord)
                ON true
            INNER JOIN pg_catalog.pg_attribute AS a
                ON a.attrelid = c.oid AND a.attnum = k.attnum
            WHERE n.nspname = @schema
              AND c.relname = @table
              AND i.indisunique
              AND i.indisvalid
              AND i.indislive
              AND i.indpred IS NULL
              AND i.indexprs IS NULL
            """;
        var rows = (await connection.QueryAsync<IndexColumnRow>(
            new CommandDefinition(
                uniqueIndexColumnsSql,
                new { schema, table },
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct))).ToArray();

        EnsureMatchingNotNullUniqueIndex(rows, keyColumns);
    }

    private static async Task ValidateSqlServerTargetAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string canonicalTargetTable,
        IReadOnlyList<string> keyColumns,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var (schema, table) = SplitSchemaAndTable(canonicalTargetTable, null);

        const string updateTriggerCheckSql = """
            SELECT CONVERT(bit, CASE WHEN NOT EXISTS (
                SELECT 1
                FROM sys.triggers AS tr
                INNER JOIN sys.trigger_events AS ev ON ev.object_id = tr.object_id
                INNER JOIN sys.tables AS t ON t.object_id = tr.parent_id
                INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
                WHERE s.name = COALESCE(@schema, SCHEMA_NAME())
                  AND t.name = @table
                  AND tr.is_disabled = 0
                  AND ev.type_desc = N'UPDATE'
            ) THEN 1 ELSE 0 END)
            """;
        var noUpdateTriggers = await connection.QuerySingleAsync<bool>(
            new CommandDefinition(
                updateTriggerCheckSql,
                new { schema, table },
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        if (!noUpdateTriggers)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        const string uniqueIndexColumnsSql = """
            SELECT
                CONVERT(bigint, i.index_id) AS IndexId,
                c.name AS ColumnName,
                CONVERT(bit, CASE WHEN c.is_nullable = 0 THEN 1 ELSE 0 END) AS IsNotNull
            FROM sys.indexes AS i
            INNER JOIN sys.tables AS t ON t.object_id = i.object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            INNER JOIN sys.index_columns AS ic
                ON ic.object_id = i.object_id
               AND ic.index_id = i.index_id
               AND ic.key_ordinal > 0
               AND ic.is_included_column = 0
            INNER JOIN sys.columns AS c
                ON c.object_id = ic.object_id
               AND c.column_id = ic.column_id
            WHERE s.name = COALESCE(@schema, SCHEMA_NAME())
              AND t.name = @table
              AND (i.is_primary_key = 1 OR i.is_unique = 1)
              AND i.is_disabled = 0
              AND i.is_hypothetical = 0
              AND i.has_filter = 0
            """;
        var rows = (await connection.QueryAsync<IndexColumnRow>(
            new CommandDefinition(
                uniqueIndexColumnsSql,
                new { schema, table },
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct))).ToArray();

        EnsureMatchingNotNullUniqueIndex(rows, keyColumns);
    }

    private static async Task ValidateMySqlTargetAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string canonicalTargetTable,
        IReadOnlyList<string> keyColumns,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var (schema, table) = SplitSchemaAndTable(canonicalTargetTable, connection.Database);

        const string updateTriggerCountSql = """
            SELECT COUNT(*)
            FROM information_schema.TRIGGERS
            WHERE BINARY EVENT_OBJECT_SCHEMA = BINARY @schema
              AND BINARY EVENT_OBJECT_TABLE = BINARY @table
              AND EVENT_MANIPULATION = 'UPDATE'
            """;
        var triggerCount = await connection.QuerySingleAsync<int>(
            new CommandDefinition(
                updateTriggerCountSql,
                new { schema, table },
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        if (triggerCount != 0)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        const string uniqueIndexColumnsSql = """
            SELECT
                s.INDEX_NAME AS IndexName,
                s.COLUMN_NAME AS ColumnName,
                CASE WHEN c.IS_NULLABLE = 'NO' AND s.SUB_PART IS NULL THEN 1 ELSE 0 END AS IsNotNull
            FROM information_schema.STATISTICS AS s
            LEFT JOIN information_schema.COLUMNS AS c
                ON BINARY c.TABLE_SCHEMA = BINARY s.TABLE_SCHEMA
               AND BINARY c.TABLE_NAME = BINARY s.TABLE_NAME
               AND BINARY c.COLUMN_NAME = BINARY s.COLUMN_NAME
            WHERE BINARY s.TABLE_SCHEMA = BINARY @schema
              AND BINARY s.TABLE_NAME = BINARY @table
              AND s.NON_UNIQUE = 0
            """;
        var mySqlRows = (await connection.QueryAsync<MySqlIndexColumnRow>(
            new CommandDefinition(
                uniqueIndexColumnsSql,
                new { schema, table },
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct))).ToArray();

        // MySQL INSERT ... ON DUPLICATE KEY UPDATE 会对表上任意唯一键触发；
        // 要求目标表仅存在一个唯一索引/主键且精确匹配 keyColumns，避免旁路唯一索引误合并。
        var distinctIndexes = mySqlRows
            .Select(r => r.IndexName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctIndexes.Length != 1)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var normalized = mySqlRows
            .Select(r => new IndexColumnRow
            {
                IndexId = 1,
                ColumnName = r.ColumnName,
                IsNotNull = r.IsNotNull,
            })
            .ToArray();
        EnsureMatchingNotNullUniqueIndex(normalized, keyColumns);
    }

    private static void EnsureMatchingNotNullUniqueIndex(
        IReadOnlyList<IndexColumnRow> rows,
        IReadOnlyList<string> keyColumns)
    {
        var expected = new HashSet<string>(keyColumns, StringComparer.OrdinalIgnoreCase);
        var matched = rows
            .GroupBy(r => r.IndexId)
            .Any(group =>
            {
                var cols = group.ToArray();
                if (cols.Length != expected.Count || cols.Any(c => !c.IsNotNull || string.IsNullOrWhiteSpace(c.ColumnName)))
                {
                    return false;
                }

                var actual = cols
                    .Select(c => c.ColumnName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return actual.SetEquals(expected);
            });

        if (!matched)
        {
            throw EtlPipelineRunner.InvalidInput();
        }
    }

    private static (string? Schema, string Table) SplitSchemaAndTable(
        string targetTable,
        string? defaultSchema)
    {
        var parts = targetTable.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            ? (parts[0], parts[1])
            : (defaultSchema, parts[^1]);
    }

    private sealed class IndexColumnRow
    {
        public long IndexId { get; set; }
        public string ColumnName { get; set; } = string.Empty;
        public bool IsNotNull { get; set; }
    }

    private sealed class MySqlIndexColumnRow
    {
        public string IndexName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public bool IsNotNull { get; set; }
    }
}
