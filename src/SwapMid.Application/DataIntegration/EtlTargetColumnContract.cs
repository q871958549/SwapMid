using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace SwapMid.DataIntegration;

/// <summary>
/// 源投影与目标可写列契约。它在创建 staging 前拒绝未知列、生成列/自增列写入，
/// 并要求 truncate 覆盖全部普通可写列，避免整列静默回落到 NULL/default。
/// </summary>
internal static class EtlTargetColumnContract
{
    internal sealed record Column(
        string Name,
        bool IsWritable,
        bool IsIdentity);

    internal static async Task ValidateAsync(
        DbConnection connection,
        string targetTable,
        IReadOnlyList<string> sourceColumns,
        bool truncate,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        IReadOnlyList<Column>? targetColumns = null;
        if (connection is SqlConnection &&
            targetProbe.NormalizedProvider is "sqlserver" or "mssql")
        {
            const string sql = """
                SELECT
                    c.name,
                    CONVERT(bit, CASE
                        WHEN c.is_identity = 0
                         AND c.is_computed = 0
                         AND c.system_type_id <> 189
                         AND c.generated_always_type = 0
                         AND c.is_hidden = 0
                         AND c.is_column_set = 0
                        THEN 1 ELSE 0 END) AS is_writable,
                    CONVERT(bit, c.is_identity) AS is_identity
                FROM sys.tables AS t
                INNER JOIN sys.columns AS c ON c.object_id = t.object_id
                WHERE t.object_id = OBJECT_ID(@targetTable, N'U')
                ORDER BY c.column_id
                """;
            targetColumns = await ReadColumnsAsync(
                connection,
                sql,
                targetTable,
                commandTimeoutSeconds,
                ct);
        }
        else if (connection is MySqlConnection &&
                 targetProbe.NormalizedProvider is "mysql" or "mariadb")
        {
            var (schema, table) = SplitCanonicalTarget(targetTable);
            const string sql = """
                SELECT
                    COLUMN_NAME,
                    CASE
                        WHEN LOWER(EXTRA) LIKE '%auto_increment%'
                          OR COALESCE(GENERATION_EXPRESSION, '') <> ''
                        THEN 0 ELSE 1
                    END AS is_writable,
                    CASE
                        WHEN LOWER(EXTRA) LIKE '%auto_increment%'
                        THEN 1 ELSE 0
                    END AS is_identity
                FROM information_schema.COLUMNS
                WHERE BINARY TABLE_SCHEMA = BINARY @schema
                  AND BINARY TABLE_NAME = BINARY @table
                ORDER BY ORDINAL_POSITION
                """;
            targetColumns = await ReadColumnsAsync(
                connection,
                sql,
                targetTable: null,
                commandTimeoutSeconds,
                ct,
                schema,
                table);
        }
        else if (connection is NpgsqlConnection &&
                 targetProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql")
        {
            var (schema, table) = SplitCanonicalTarget(targetTable);
            const string sql = """
                SELECT
                    a.attname,
                    (a.attidentity = '' AND a.attgenerated = '') AS is_writable,
                    (a.attidentity <> '') AS is_identity
                FROM pg_catalog.pg_attribute AS a
                INNER JOIN pg_catalog.pg_class AS c ON c.oid = a.attrelid
                INNER JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema
                  AND c.relname = @table
                  AND a.attnum > 0
                  AND NOT a.attisdropped
                ORDER BY a.attnum
                """;
            targetColumns = await ReadColumnsAsync(
                connection,
                sql,
                targetTable: null,
                commandTimeoutSeconds,
                ct,
                schema,
                table);
        }

        // SQLite/recording fake 只验证事务边界；生产 provider 必须取得真实列目录。
        if (targetColumns != null)
        {
            ValidateProjection(
                sourceColumns,
                targetColumns,
                truncate);
        }
    }

    internal static void ValidateProjection(
        IReadOnlyList<string> sourceColumns,
        IReadOnlyList<Column> targetColumns,
        bool truncate)
    {
        if (targetColumns.Count == 0)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var targetByName =
            new Dictionary<string, Column>(
                StringComparer.OrdinalIgnoreCase);
        foreach (var targetColumn in targetColumns)
        {
            if (string.IsNullOrWhiteSpace(targetColumn.Name) ||
                !targetByName.TryAdd(
                    targetColumn.Name,
                    targetColumn))
            {
                throw EtlPipelineRunner.InvalidInput();
            }
        }

        foreach (var sourceColumn in sourceColumns)
        {
            if (!targetByName.TryGetValue(
                    sourceColumn,
                    out var targetColumn) ||
                !targetColumn.IsWritable)
            {
                throw EtlPipelineRunner.InvalidInput();
            }
        }

        if (!truncate)
        {
            return;
        }

        // DELETE 不会 reseed identity/auto_increment；让全量替换生成一套新 ID
        // 会破坏下游引用语义，未设计显式 identity 策略前一律拒绝。
        if (targetColumns.Any(column => column.IsIdentity))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var writable = targetColumns
            .Where(column => column.IsWritable)
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (sourceColumns.Count != writable.Count ||
            sourceColumns.Any(column => !writable.Contains(column)))
        {
            throw EtlPipelineRunner.InvalidInput();
        }
    }

    private static async Task<IReadOnlyList<Column>> ReadColumnsAsync(
        DbConnection connection,
        string sql,
        string? targetTable,
        int commandTimeoutSeconds,
        CancellationToken ct,
        string? schema = null,
        string? table = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;
        AddParameter(command, "targetTable", targetTable);
        AddParameter(command, "schema", schema);
        AddParameter(command, "table", table);

        var columns = new List<Column>();
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess | CommandBehavior.SingleResult,
            ct);
        while (await reader.ReadAsync(ct))
        {
            if (await reader.IsDBNullAsync(0, ct))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            columns.Add(new Column(
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2)));
        }

        return columns;
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        string? value)
    {
        if (value == null)
        {
            return;
        }

        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static (string Schema, string Table) SplitCanonicalTarget(
        string targetTable)
    {
        var parts = targetTable.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return (parts[0], parts[1]);
    }
}
