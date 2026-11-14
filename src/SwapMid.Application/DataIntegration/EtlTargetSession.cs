using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace SwapMid.DataIntegration;

/// <summary>
/// 目标连接会话的规范化与 SET profile。把名称解析、staging 严格模式和正式 DML
/// 模式集中在同一边界，避免不同阶段各自解释目标表或继承登录默认值。
/// </summary>
internal static class EtlTargetSession
{
    internal static Task ConfigureForStagingAsync(
        DbConnection connection,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        if (connection is not SqlConnection ||
            targetProbe.NormalizedProvider is not ("sqlserver" or "mssql"))
        {
            return Task.CompletedTask;
        }

        return connection.ExecuteAsync(new CommandDefinition(
            """
            SET ANSI_NULLS ON;
            SET ANSI_PADDING ON;
            SET ANSI_WARNINGS ON;
            SET ARITHABORT ON;
            SET CONCAT_NULL_YIELDS_NULL ON;
            SET QUOTED_IDENTIFIER ON;
            SET NUMERIC_ROUNDABORT ON;
            SET XACT_ABORT ON;
            """,
            commandTimeout: commandTimeoutSeconds,
            cancellationToken: ct));
    }

    internal static Task PrepareForFormalWriteAsync(
        DbConnection connection,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        if (connection is not SqlConnection ||
            targetProbe.NormalizedProvider is not ("sqlserver" or "mssql"))
        {
            return Task.CompletedTask;
        }

        // staging 对账完成后已证明值可无损进入目标类型。正式 DML 恢复 SQL Server
        // 对索引视图/计算列索引要求的 OFF 配置，避免兼容性错误。
        return connection.ExecuteAsync(new CommandDefinition(
            "SET NUMERIC_ROUNDABORT OFF;",
            commandTimeout: commandTimeoutSeconds,
            cancellationToken: ct));
    }

    internal static async Task<string> ResolveCanonicalTargetTableAsync(
        DbConnection connection,
        string targetTable,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct,
        DbTransaction? transaction = null)
    {
        if (connection is SqlConnection &&
            targetProbe.NormalizedProvider is "sqlserver" or "mssql")
        {
            const string sql = """
                SELECT s.name + N'.' + t.name
                FROM sys.tables AS t
                INNER JOIN sys.schemas AS s
                    ON s.schema_id = t.schema_id
                WHERE t.object_id = OBJECT_ID(@targetTable, N'U')
                """;
            var resolved =
                await connection.QuerySingleOrDefaultAsync<string>(
                    new CommandDefinition(
                        sql,
                        new { targetTable },
                        transaction: transaction,
                        commandTimeout: commandTimeoutSeconds,
                        cancellationToken: ct));
            return ValidateResolved(resolved);
        }

        if (connection is MySqlConnection &&
            targetProbe.NormalizedProvider is "mysql" or "mariadb")
        {
            const string identitySql = """
                SELECT
                    DATABASE() AS DatabaseName,
                    CONVERT(
                        @@lower_case_table_names,
                        SIGNED) AS LowerCaseTableNames
                """;
            var databaseIdentity =
                await connection.QuerySingleAsync<MySqlDatabaseIdentity>(
                    new CommandDefinition(
                        identitySql,
                        transaction: transaction,
                        commandTimeout: commandTimeoutSeconds,
                        cancellationToken: ct));
            if (string.IsNullOrWhiteSpace(
                    databaseIdentity.DatabaseName) ||
                databaseIdentity.LowerCaseTableNames is < 0 or > 2)
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            var parts = targetTable.Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries);
            var schema = parts.Length == 2
                ? parts[0]
                : databaseIdentity.DatabaseName;
            var table = parts[^1];
            if (!IsSameMySqlDatabase(
                    schema,
                    databaseIdentity.DatabaseName,
                    databaseIdentity.LowerCaseTableNames))
            {
                // 临时表属于默认 database；跨库目标会让权限和 staging 落点分离。
                throw EtlPipelineRunner.InvalidInput();
            }

            var namePredicate =
                databaseIdentity.LowerCaseTableNames == 0
                    ? """
                      BINARY TABLE_SCHEMA = BINARY @schema
                      AND BINARY TABLE_NAME = BINARY @table
                      """
                    : """
                      TABLE_SCHEMA = @schema
                      AND TABLE_NAME = @table
                      """;
            var sql = $"""
                SELECT CONCAT(TABLE_SCHEMA, '.', TABLE_NAME)
                FROM information_schema.TABLES
                WHERE {namePredicate}
                  AND TABLE_TYPE = 'BASE TABLE'
                """;
            var resolved =
                await connection.QuerySingleOrDefaultAsync<string>(
                    new CommandDefinition(
                        sql,
                        new { schema, table },
                        transaction: transaction,
                        commandTimeout: commandTimeoutSeconds,
                        cancellationToken: ct));
            return ValidateResolved(resolved);
        }

        if (connection is NpgsqlConnection &&
            targetProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql")
        {
            var parts = targetTable.Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 1 or > 2)
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            var schema = parts.Length == 2 ? parts[0] : "public";
            var table = parts[^1];
            const string pgSql = """
                SELECT n.nspname || '.' || c.relname
                FROM pg_catalog.pg_class AS c
                INNER JOIN pg_catalog.pg_namespace AS n
                    ON n.oid = c.relnamespace
                WHERE n.nspname = @schema
                  AND c.relname = @table
                  AND c.relkind = 'r'
                  AND n.nspname NOT IN ('pg_catalog', 'information_schema')
                  AND n.nspname NOT LIKE 'pg_toast%'
                  AND n.nspname NOT LIKE 'pg_temp%'
                """;
            var resolved =
                await connection.QuerySingleOrDefaultAsync<string>(
                    new CommandDefinition(
                        pgSql,
                        new { schema, table },
                        transaction: transaction,
                        commandTimeout: commandTimeoutSeconds,
                        cancellationToken: ct));
            return ValidateResolved(resolved);
        }

        return targetTable;
    }

    internal static bool IsSameMySqlDatabase(
        string requested,
        string current,
        int lowerCaseTableNames) =>
        lowerCaseTableNames switch
        {
            0 => string.Equals(
                requested,
                current,
                StringComparison.Ordinal),
            1 or 2 => string.Equals(
                requested,
                current,
                StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static string ValidateResolved(string? resolved)
    {
        if (!EtlTransformConfig.IsValidTargetTable(resolved))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return resolved!;
    }

    private sealed class MySqlDatabaseIdentity
    {
        public string? DatabaseName { get; set; }
        public int LowerCaseTableNames { get; set; }
    }
}
