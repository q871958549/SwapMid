using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.DataIntegration;

internal static class EtlTargetSchemaInspector
{
    internal static async Task<List<PreflightColumnDto>> ProbeSourceColumnsAsync(
        DbConnection connection,
        string normalizedProvider,
        string sourceQuery,
        CancellationToken cancellationToken)
    {
        var isSqlite = connection.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            if (!isSqlite && normalizedProvider is "postgresql" or "postgres" or "npgsql")
            {
                await using var roCmd = connection.CreateCommand();
                roCmd.Transaction = tx;
                roCmd.CommandTimeout = 8;
                roCmd.CommandText = "SET TRANSACTION READ ONLY;";
                await roCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandTimeout = 8;
            cmd.CommandText = sourceQuery;

            await using var reader = await cmd.ExecuteReaderAsync(
                CommandBehavior.SchemaOnly | CommandBehavior.SingleResult,
                cancellationToken);

            var columns = new List<PreflightColumnDto>();
            if (reader.CanGetColumnSchema())
            {
                foreach (var col in reader.GetColumnSchema())
                {
                    var name = col.ColumnName?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(name)) continue;
                    columns.Add(new PreflightColumnDto
                    {
                        Name = name,
                        DataType = !string.IsNullOrWhiteSpace(col.DataTypeName)
                            ? col.DataTypeName
                            : col.DataType?.Name ?? "unknown",
                        IsNullable = col.AllowDBNull ?? true,
                    });
                }
            }
            else
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i)?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(name)) continue;
                    columns.Add(new PreflightColumnDto
                    {
                        Name = name,
                        DataType = reader.GetDataTypeName(i),
                        IsNullable = true,
                    });
                }
            }

            return columns;
        }
        finally
        {
            await tx.RollbackAsync(CancellationToken.None);
        }
    }

    internal static async Task<List<PreflightColumnDto>> InspectTargetTableAsync(
        DbConnection connection,
        string normalizedProvider,
        string targetTable,
        CancellationToken cancellationToken)
    {
        var (schemaName, tableName) = SplitTableIdentifier(targetTable);
        if (connection.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return await InspectSqliteAsync(connection, tableName, cancellationToken);
        }

        return normalizedProvider switch
        {
            "postgresql" or "postgres" or "npgsql" =>
                await InspectPostgreSqlAsync(connection, schemaName ?? "public", tableName, cancellationToken),
            "mysql" or "mariadb" =>
                await InspectMySqlAsync(connection, schemaName, tableName, cancellationToken),
            "sqlite" =>
                await InspectSqliteAsync(connection, tableName, cancellationToken),
            _ =>
                await InspectSqlServerAsync(connection, schemaName ?? "dbo", tableName, cancellationToken),
        };
    }

    private static (string? Schema, string Table) SplitTableIdentifier(string raw)
    {
        var parts = raw.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return (parts[^2], parts[^1]);
        }

        return (null, parts.Length == 1 ? parts[0] : raw.Trim());
    }

    private static async Task<List<PreflightColumnDto>> InspectPostgreSqlAsync(
        DbConnection connection,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = 8;
        cmd.CommandText = @"
SELECT a.attname AS col_name,
       format_type(a.atttypid, a.atttypmod) AS data_type,
       NOT a.attnotnull AS is_nullable,
       COALESCE(ix.is_pk, false) AS is_pk,
       COALESCE(ix.is_unique, false) AS is_unique,
       (a.atthasdef OR a.attidentity <> '' OR a.attgenerated <> '') AS has_default
FROM pg_attribute a
JOIN pg_class c ON c.oid = a.attrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
LEFT JOIN (
    SELECT a2.attnum,
           bool_or(i.indisprimary) AS is_pk,
           bool_or(i.indisunique AND i.indpred IS NULL) AS is_unique
    FROM pg_index i
    JOIN pg_class c2 ON c2.oid = i.indrelid
    JOIN pg_namespace n2 ON n2.oid = c2.relnamespace
    JOIN pg_attribute a2 ON a2.attrelid = c2.oid AND a2.attnum = ANY(i.indkey)
    WHERE n2.nspname = @schemaName AND c2.relname = @tableName AND i.indisvalid
    GROUP BY a2.attnum
) ix ON ix.attnum = a.attnum
WHERE n.nspname = @schemaName
  AND c.relname = @tableName
  AND c.relkind IN ('r', 'p')
  AND a.attnum > 0
  AND NOT a.attisdropped
ORDER BY a.attnum;";
        AddParam(cmd, "@schemaName", schemaName);
        AddParam(cmd, "@tableName", tableName);
        return await ReadColumnsAsync(cmd, cancellationToken);
    }

    private static async Task<List<PreflightColumnDto>> InspectSqlServerAsync(
        DbConnection connection,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = 8;
        cmd.CommandText = @"
SELECT c.name AS col_name,
       ty.name AS data_type,
       c.is_nullable AS is_nullable,
       CAST(COALESCE(ix.is_pk, 0) AS bit) AS is_pk,
       CAST(COALESCE(ix.is_unique, 0) AS bit) AS is_unique,
       CAST(CASE WHEN c.default_object_id <> 0 OR c.is_identity = 1 OR c.is_computed = 1 THEN 1 ELSE 0 END AS bit) AS has_default
FROM sys.columns c
JOIN sys.tables t ON t.object_id = c.object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN (
    SELECT ic.column_id,
           MAX(CASE WHEN i.is_primary_key = 1 THEN 1 ELSE 0 END) AS is_pk,
           MAX(CASE WHEN i.is_unique = 1 AND i.has_filter = 0 THEN 1 ELSE 0 END) AS is_unique
    FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
    JOIN sys.tables t2 ON t2.object_id = i.object_id
    JOIN sys.schemas s2 ON s2.schema_id = t2.schema_id
    WHERE s2.name = @schemaName AND t2.name = @tableName AND i.is_disabled = 0
    GROUP BY ic.column_id
) ix ON ix.column_id = c.column_id
WHERE s.name = @schemaName AND t.name = @tableName
ORDER BY c.column_id;";
        AddParam(cmd, "@schemaName", schemaName);
        AddParam(cmd, "@tableName", tableName);
        return await ReadColumnsAsync(cmd, cancellationToken);
    }

    private static async Task<List<PreflightColumnDto>> InspectMySqlAsync(
        DbConnection connection,
        string? schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = 8;
        cmd.CommandText = @"
SELECT c.COLUMN_NAME AS col_name,
       c.DATA_TYPE AS data_type,
       CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS is_nullable,
       CASE WHEN c.COLUMN_KEY = 'PRI' THEN 1 ELSE 0 END AS is_pk,
       CASE WHEN c.COLUMN_KEY IN ('PRI', 'UNI') THEN 1 ELSE 0 END AS is_unique,
       CASE WHEN c.COLUMN_DEFAULT IS NOT NULL OR c.EXTRA LIKE '%auto_increment%' THEN 1 ELSE 0 END AS has_default
FROM information_schema.COLUMNS c
WHERE c.TABLE_SCHEMA = COALESCE(@schemaName, DATABASE())
  AND c.TABLE_NAME = @tableName
ORDER BY c.ORDINAL_POSITION;";
        AddParam(cmd, "@schemaName", (object?)schemaName ?? DBNull.Value);
        AddParam(cmd, "@tableName", tableName);
        return await ReadColumnsAsync(cmd, cancellationToken);
    }

    private static async Task<List<PreflightColumnDto>> InspectSqliteAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var uniqueCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var idxCmd = connection.CreateCommand())
        {
            idxCmd.CommandText = $"PRAGMA index_list(\"{tableName.Replace("\"", "\"\"")}\");";
            await using var idxReader = await idxCmd.ExecuteReaderAsync(cancellationToken);
            var uniqueIndexNames = new List<string>();
            while (await idxReader.ReadAsync(cancellationToken))
            {
                if (Convert.ToInt32(idxReader["unique"]) == 1 &&
                    Convert.ToInt32(idxReader["partial"]) == 0)
                {
                    uniqueIndexNames.Add(Convert.ToString(idxReader["name"]) ?? string.Empty);
                }
            }

            foreach (var indexName in uniqueIndexNames.Where(n => n.Length > 0))
            {
                await using var infoCmd = connection.CreateCommand();
                infoCmd.CommandText = $"PRAGMA index_info(\"{indexName.Replace("\"", "\"\"")}\");";
                await using var infoReader = await infoCmd.ExecuteReaderAsync(cancellationToken);
                while (await infoReader.ReadAsync(cancellationToken))
                {
                    var colName = Convert.ToString(infoReader["name"]);
                    if (!string.IsNullOrWhiteSpace(colName)) uniqueCols.Add(colName);
                }
            }
        }

        var columns = new List<PreflightColumnDto>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\");";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = Convert.ToString(reader["name"]) ?? string.Empty;
            var isPk = Convert.ToInt32(reader["pk"]) > 0;
            var notNull = Convert.ToInt32(reader["notnull"]) != 0;
            var hasDefault = reader["dflt_value"] is not DBNull;
            columns.Add(new PreflightColumnDto
            {
                Name = name,
                DataType = Convert.ToString(reader["type"]) ?? "BLOB",
                IsNullable = !notNull && !isPk,
                IsPrimaryKey = isPk,
                IsUniqueIndexed = isPk || uniqueCols.Contains(name),
                HasDefaultOrIdentity = hasDefault || isPk,
            });
        }
        return columns;
    }

    private static async Task<List<PreflightColumnDto>> ReadColumnsAsync(
        DbCommand cmd,
        CancellationToken cancellationToken)
    {
        var result = new List<PreflightColumnDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PreflightColumnDto
            {
                Name = reader.GetString(0),
                DataType = reader.IsDBNull(1) ? "unknown" : reader.GetString(1),
                IsNullable = Convert.ToBoolean(reader.GetValue(2)),
                IsPrimaryKey = Convert.ToBoolean(reader.GetValue(3)),
                IsUniqueIndexed = Convert.ToBoolean(reader.GetValue(4)),
                HasDefaultOrIdentity = Convert.ToBoolean(reader.GetValue(5)),
            });
        }
        return result;
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
