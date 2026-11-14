using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace SwapMid.DataIntegration;

/// <summary>
/// 目标表模式指纹。暂存表创建前记录一次，正式写入事务取得目标锁后再读取一次，
/// 防止长时间源读取期间 ALTER/DROP+CREATE 让旧暂存形状写入新目标定义。
/// </summary>
internal static class EtlTargetSchemaFingerprint
{
    internal static async Task<string> ReadAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string targetTable,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        if (connection is SqlConnection &&
            targetProbe.NormalizedProvider is "sqlserver" or "mssql")
        {
            var (schema, table) = SplitTarget(
                targetTable,
                defaultSchema: null);
            const string sql = """
                SELECT
                    CONVERT(bigint, t.object_id) AS table_object_id,
                    CONVERT(nvarchar(33), t.create_date, 126) AS table_create_date,
                    CONVERT(nvarchar(33), t.modify_date, 126) AS table_modify_date,
                    CONVERT(int, c.column_id) AS column_ordinal,
                    c.name AS column_name,
                    CONVERT(int, c.user_type_id) AS user_type_id,
                    CONVERT(int, c.system_type_id) AS system_type_id,
                    ty.name AS type_name,
                    CONVERT(int, c.max_length) AS max_length,
                    CONVERT(int, c.precision) AS numeric_precision,
                    CONVERT(int, c.scale) AS numeric_scale,
                    c.collation_name,
                    CONVERT(bit, c.is_nullable) AS is_nullable,
                    CONVERT(bit, c.is_ansi_padded) AS is_ansi_padded,
                    CONVERT(bit, c.is_rowguidcol) AS is_rowguidcol,
                    CONVERT(bit, c.is_identity) AS is_identity,
                    CONVERT(bit, c.is_computed) AS is_computed,
                    CONVERT(bit, c.is_filestream) AS is_filestream,
                    CONVERT(bit, c.is_sparse) AS is_sparse,
                    dc.definition AS default_definition,
                    cc.definition AS computed_definition
                FROM sys.tables AS t
                INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
                INNER JOIN sys.columns AS c ON c.object_id = t.object_id
                INNER JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
                LEFT JOIN sys.default_constraints AS dc
                    ON dc.object_id = c.default_object_id
                LEFT JOIN sys.computed_columns AS cc
                    ON cc.object_id = c.object_id
                   AND cc.column_id = c.column_id
                WHERE s.name = COALESCE(@schema, SCHEMA_NAME())
                  AND t.name = @table
                ORDER BY c.column_id
                """;
            return await HashRowsAsync(
                connection,
                transaction,
                sql,
                schema,
                table,
                "sqlserver",
                commandTimeoutSeconds,
                ct);
        }

        if (connection is MySqlConnection &&
            targetProbe.NormalizedProvider is "mysql" or "mariadb")
        {
            var (schema, table) = SplitTarget(
                targetTable,
                connection.Database);
            const string sql = """
                SELECT
                    t.TABLE_TYPE,
                    t.ENGINE,
                    t.VERSION,
                    t.ROW_FORMAT,
                    t.TABLE_COLLATION,
                    t.CREATE_OPTIONS,
                    DATE_FORMAT(t.CREATE_TIME, '%Y-%m-%dT%H:%i:%s.%f'),
                    c.ORDINAL_POSITION,
                    c.COLUMN_NAME,
                    c.COLUMN_DEFAULT,
                    c.IS_NULLABLE,
                    c.DATA_TYPE,
                    c.CHARACTER_MAXIMUM_LENGTH,
                    c.CHARACTER_OCTET_LENGTH,
                    c.NUMERIC_PRECISION,
                    c.NUMERIC_SCALE,
                    c.DATETIME_PRECISION,
                    c.CHARACTER_SET_NAME,
                    c.COLLATION_NAME,
                    c.COLUMN_TYPE,
                    c.COLUMN_KEY,
                    c.EXTRA,
                    c.COLUMN_COMMENT,
                    c.GENERATION_EXPRESSION
                FROM information_schema.TABLES AS t
                INNER JOIN information_schema.COLUMNS AS c
                    ON BINARY c.TABLE_SCHEMA =
                       BINARY t.TABLE_SCHEMA
                   AND BINARY c.TABLE_NAME =
                       BINARY t.TABLE_NAME
                WHERE BINARY t.TABLE_SCHEMA = BINARY @schema
                  AND BINARY t.TABLE_NAME = BINARY @table
                ORDER BY c.ORDINAL_POSITION
                """;
            var columnFingerprint = await HashRowsAsync(
                connection,
                transaction,
                sql,
                schema,
                table,
                "mysql",
                commandTimeoutSeconds,
                ct);
            var createDefinition = await ReadMySqlCreateDefinitionAsync(
                connection,
                transaction,
                targetTable,
                targetProbe,
                commandTimeoutSeconds,
                ct);
            using var composite =
                IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Append(composite, "mysql-schema-v2");
            Append(composite, columnFingerprint);
            Append(composite, createDefinition);
            return Convert.ToHexString(composite.GetHashAndReset());
        }

        if (connection is NpgsqlConnection &&
            targetProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql")
        {
            var (schema, table) = SplitTarget(
                targetTable,
                defaultSchema: "public");
            const string pgSql = """
                SELECT
                    c.oid::bigint AS table_oid,
                    c.relfilenode::bigint AS table_relfilenode,
                    c.relpersistence::text AS table_persistence,
                    c.relkind::text AS table_kind,
                    a.attnum::int AS column_ordinal,
                    a.attname AS column_name,
                    pg_catalog.format_type(a.atttypid, a.atttypmod) AS formatted_type,
                    a.attnotnull AS is_not_null,
                    a.attidentity::text AS identity_kind,
                    a.attgenerated::text AS generated_kind,
                    a.attcollation::bigint AS collation_oid,
                    pg_catalog.pg_get_expr(ad.adbin, ad.adrelid) AS default_expr,
                    (
                        SELECT COALESCE(
                            string_agg(
                                con.conname || ':' || con.contype::text || ':' ||
                                pg_catalog.pg_get_constraintdef(con.oid, true),
                                '|' ORDER BY con.conname),
                            '')
                        FROM pg_catalog.pg_constraint AS con
                        WHERE con.conrelid = c.oid
                    ) AS constraints_summary
                FROM pg_catalog.pg_class AS c
                INNER JOIN pg_catalog.pg_namespace AS n
                    ON n.oid = c.relnamespace
                INNER JOIN pg_catalog.pg_attribute AS a
                    ON a.attrelid = c.oid
                LEFT JOIN pg_catalog.pg_attrdef AS ad
                    ON ad.adrelid = a.attrelid
                   AND ad.adnum = a.attnum
                WHERE n.nspname = @schema
                  AND c.relname = @table
                  AND c.relkind = 'r'
                  AND a.attnum > 0
                  AND NOT a.attisdropped
                ORDER BY a.attnum
                """;
            return await HashRowsAsync(
                connection,
                transaction,
                pgSql,
                schema,
                table,
                "postgresql",
                commandTimeoutSeconds,
                ct);
        }

        // SQLite/recording fake 只承担事务语义测试；生产 provider 不会走这里。
        return $"test:{targetTable}";
    }

    private static async Task<string> HashRowsAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        string? schema,
        string table,
        string provider,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;
        command.Transaction = transaction;
        AddParameter(command, "schema", schema);
        AddParameter(command, "table", table);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, provider);
        var rowCount = 0;
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess | CommandBehavior.SingleResult,
            ct);
        while (await reader.ReadAsync(ct))
        {
            rowCount++;
            Append(hash, $"row:{rowCount}");
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                if (await reader.IsDBNullAsync(ordinal, ct))
                {
                    Append(hash, null);
                    continue;
                }

                var value = reader.GetValue(ordinal);
                Append(
                    hash,
                    value is IFormattable formattable
                        ? formattable.ToString(
                            null,
                            CultureInfo.InvariantCulture)
                        : value.ToString());
            }
        }

        if (rowCount == 0)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<string> ReadMySqlCreateDefinitionAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string targetTable,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var backslashEscapes =
            await connection.QuerySingleAsync<bool>(
                new CommandDefinition(
                    """
                    SELECT FIND_IN_SET(
                        'NO_BACKSLASH_ESCAPES',
                        @@SESSION.sql_mode) = 0
                    """,
                    transaction: transaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: ct));
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SHOW CREATE TABLE {RelationalDataSourceConnection.QuoteIdentifier(targetTable, targetProbe)}";
        command.CommandTimeout = commandTimeoutSeconds;
        command.Transaction = transaction;
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow | CommandBehavior.SingleResult,
            ct);
        if (!await reader.ReadAsync(ct) ||
            reader.FieldCount < 2 ||
            await reader.IsDBNullAsync(1, ct))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var definition = reader.GetString(1);
        if (await reader.ReadAsync(ct))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        if (EtlMySqlCreateDefinition
            .HasUnsupportedTemporaryCloneShape(
                definition,
                backslashEscapes))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return EtlMySqlCreateDefinition.NormalizeForFingerprint(
            definition,
            backslashEscapes);
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        string? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? (object)DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static void Append(
        IncrementalHash hash,
        string? value)
    {
        if (value == null)
        {
            hash.AppendData([0]);
            return;
        }

        hash.AppendData([1]);
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static (string? Schema, string Table) SplitTarget(
        string targetTable,
        string? defaultSchema)
    {
        var parts = targetTable.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries);
        return (
            parts.Length == 2 ? parts[0] : defaultSchema,
            parts[^1]);
    }
}
