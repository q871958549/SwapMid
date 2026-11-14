using System;
using System.Collections.Generic;
using System.Data;
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
/// 目标会话临时暂存表：先在正式表事务外承接有界源数据，源 reader 完全关闭后，
/// 再在短事务内分批转入正式表。随机内部行号既避免与业务列碰撞，也限制单批 trigger
/// 与锁升级压力。
/// </summary>
internal static class EtlTargetStagingWriter
{
    internal sealed record Stage(string Name, string RowIdColumn);

    internal static async Task<Stage> CreateAsync(
        DbConnection connection,
        string targetTable,
        IReadOnlyList<string> columns,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var isSqlServer =
            connection is SqlConnection &&
            targetProbe.NormalizedProvider is "sqlserver" or "mssql";
        var isMySql =
            connection is MySqlConnection &&
            targetProbe.NormalizedProvider is "mysql" or "mariadb";
        var isPostgreSql =
            connection is NpgsqlConnection &&
            targetProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql";
        var stage = new Stage(
            isSqlServer ? $"#etl_stage_{suffix}" : $"etl_stage_{suffix}",
            $"etl_row_{suffix[..16]}");
        var quotedStage = QuoteStage(stage, targetProbe);
        var quotedRowId = QuoteRowId(stage, targetProbe);
        var quotedTarget =
            RelationalDataSourceConnection.QuoteIdentifier(
                targetTable,
                targetProbe);
        var quotedColumns = string.Join(
            ", ",
            columns.Select(column =>
                RelationalDataSourceConnection.QuoteIdentifierPart(
                    column,
                    targetProbe)));
        var indexName =
            RelationalDataSourceConnection.QuoteIdentifierPart(
                $"ix_etl_{suffix[..16]}",
                targetProbe);

        string createSql;
        if (isSqlServer)
        {
            var selectedColumns = string.Join(
                ", ",
                columns.Select(column =>
                    $"[etl_target].{RelationalDataSourceConnection.QuoteIdentifierPart(column, targetProbe)}"));
            createSql = $"""
                SELECT TOP (0)
                    CAST(0 AS bigint) AS {quotedRowId},
                    {selectedColumns}
                INTO {quotedStage}
                FROM {quotedTarget} AS [etl_target]
                CROSS JOIN (VALUES (0)) AS [etl_identity_guard]([marker]);
                CREATE UNIQUE CLUSTERED INDEX {indexName}
                    ON {quotedStage} ({quotedRowId});
                """;
        }
        else if (isMySql)
        {
            createSql = $"""
                CREATE TEMPORARY TABLE {quotedStage}
                    LIKE {quotedTarget};
                ALTER TABLE {quotedStage}
                    ADD COLUMN {quotedRowId} BIGINT NOT NULL FIRST,
                    ADD UNIQUE INDEX {indexName} ({quotedRowId});
                """;
        }
        else if (isPostgreSql)
        {
            createSql = $"""
                CREATE TEMP TABLE {quotedStage} AS
                SELECT
                    CAST(0 AS bigint) AS {quotedRowId},
                    {quotedColumns}
                FROM {quotedTarget}
                WHERE false;
                CREATE UNIQUE INDEX {indexName}
                    ON {quotedStage} ({quotedRowId});
                """;
        }
        else
        {
            // 测试连接使用 SQLite/可记录 fake；生产 provider 必须由上面的真实驱动分支接管。
            createSql = $"""
                CREATE TEMP TABLE {quotedStage} AS
                SELECT
                    CAST(0 AS INTEGER) AS {quotedRowId},
                    {quotedColumns}
                FROM {quotedTarget}
                WHERE 0;
                CREATE UNIQUE INDEX {indexName}
                    ON {quotedStage} ({quotedRowId});
                """;
        }

        // MySQL 列数/索引数只能做早期余量筛选，无法可靠计算 InnoDB 实际半页
        // 行宽；此时尚未读取第一行源数据，CREATE LIKE + ADD rowid 本身就是正式表外
        // 的精确能力探针。失败保留原 provider 类型，供统一 FailurePolicy 安全归类。
        await connection.ExecuteAsync(new CommandDefinition(
            createSql,
            commandTimeout: commandTimeoutSeconds,
            cancellationToken: ct));
        return stage;
    }

    internal static async Task InsertBatchAsync(
        DbConnection connection,
        Stage stage,
        long firstRowId,
        IReadOnlyList<string> columns,
        IReadOnlyList<object?[]> rows,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var quotedColumns = new[]
            {
                QuoteRowId(stage, targetProbe),
            }
            .Concat(columns.Select(column =>
                RelationalDataSourceConnection.QuoteIdentifierPart(
                    column,
                    targetProbe)))
            .ToArray();
        var parameters = new DynamicParameters();
        var valueGroups = new string[rows.Count];
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var placeholders = new string[columns.Count + 1];
            var rowIdParameter = $"r{rowIndex}";
            placeholders[0] = $"@{rowIdParameter}";
            parameters.Add(
                rowIdParameter,
                checked(firstRowId + rowIndex));
            for (var columnIndex = 0;
                 columnIndex < columns.Count;
                 columnIndex++)
            {
                var parameterName = $"p{rowIndex}_{columnIndex}";
                placeholders[columnIndex + 1] = $"@{parameterName}";
                var value = rows[rowIndex][columnIndex];
                var parameterValue =
                    NormalizeParameterValue(value);
                parameters.Add(
                    parameterName,
                    parameterValue,
                    GetDbType(parameterValue, targetProbe));
            }

            valueGroups[rowIndex] =
                $"({string.Join(", ", placeholders)})";
        }

        var insertSql =
            $"INSERT INTO {QuoteStage(stage, targetProbe)} " +
            $"({string.Join(", ", quotedColumns)}) VALUES " +
            string.Join(", ", valueGroups);
        var countedInsertSql = $"""
            SET NOCOUNT ON;
            DECLARE @etl_inserted TABLE ([marker] bit NOT NULL);
            INSERT INTO {QuoteStage(stage, targetProbe)}
                ({string.Join(", ", quotedColumns)})
            OUTPUT CAST(1 AS bit) INTO @etl_inserted
            VALUES {string.Join(", ", valueGroups)};
            SELECT COUNT_BIG(*) FROM @etl_inserted;
            """;
        var affectedRows = await ExecuteCountedInsertAsync(
            connection,
            transaction: null,
            insertSql,
            countedInsertSql,
            parameters,
            commandTimeoutSeconds,
            ct);
        if (affectedRows != rows.Count)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        await EtlStagedValueGuard.VerifyAsync(
            connection,
            stage,
            firstRowId,
            columns,
            rows,
            targetProbe,
            commandTimeoutSeconds,
            ct);
    }

    internal static async Task TransferBatchAsync(
        DbConnection connection,
        DbTransaction targetTransaction,
        Stage stage,
        string targetTable,
        IReadOnlyList<string> columns,
        long firstRowId,
        int expectedRows,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var quotedColumns = string.Join(
            ", ",
            columns.Select(column =>
                RelationalDataSourceConnection.QuoteIdentifierPart(
                    column,
                    targetProbe)));
        var parameters = new DynamicParameters();
        parameters.Add("stageStartRowId", firstRowId);
        parameters.Add(
            "stageEndRowId",
            checked(firstRowId + expectedRows));
        var insertSql = $"""
            INSERT INTO {RelationalDataSourceConnection.QuoteIdentifier(targetTable, targetProbe)}
                ({quotedColumns})
            SELECT {quotedColumns}
            FROM {QuoteStage(stage, targetProbe)}
            WHERE {QuoteRowId(stage, targetProbe)} >= @stageStartRowId
              AND {QuoteRowId(stage, targetProbe)} < @stageEndRowId
            """;
        var countedInsertSql = $"""
            SET NOCOUNT ON;
            DECLARE @etl_inserted TABLE ([marker] bit NOT NULL);
            INSERT INTO {RelationalDataSourceConnection.QuoteIdentifier(targetTable, targetProbe)}
                ({quotedColumns})
            OUTPUT CAST(1 AS bit) INTO @etl_inserted
            SELECT {quotedColumns}
            FROM {QuoteStage(stage, targetProbe)}
            WHERE {QuoteRowId(stage, targetProbe)} >= @stageStartRowId
              AND {QuoteRowId(stage, targetProbe)} < @stageEndRowId;
            SELECT COUNT_BIG(*) FROM @etl_inserted;
            """;
        var affectedRows = await ExecuteCountedInsertAsync(
            connection,
            targetTransaction,
            insertSql,
            countedInsertSql,
            parameters,
            commandTimeoutSeconds,
            ct);
        if (affectedRows != expectedRows)
        {
            // IGNORE_DUP_KEY、非严格 MySQL 转换或 trigger 不得造成少写后报告成功。
            throw EtlPipelineRunner.InvalidInput();
        }
    }

    internal static Task DropAsync(
        DbConnection connection,
        Stage stage,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        string dropSql;
        if (connection is SqlConnection)
        {
            // DROP TABLE IF EXISTS 是 SQL Server 2016+；兼容遗留 2008/2012/2014。
            dropSql = $"""
                IF OBJECT_ID(N'tempdb..{stage.Name}', N'U') IS NOT NULL
                    DROP TABLE {QuoteStage(stage, targetProbe)}
                """;
        }
        else if (connection is MySqlConnection &&
                 targetProbe.NormalizedProvider is "mysql" or "mariadb")
        {
            dropSql =
                $"DROP TEMPORARY TABLE IF EXISTS {QuoteStage(stage, targetProbe)}";
        }
        else if (connection is NpgsqlConnection &&
                 targetProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql")
        {
            dropSql =
                $"DROP TABLE IF EXISTS pg_temp.{QuoteStage(stage, targetProbe)}";
        }
        else
        {
            dropSql =
                $"DROP TABLE IF EXISTS {QuoteStage(stage, targetProbe)}";
        }

        return connection.ExecuteAsync(new CommandDefinition(
            dropSql,
            commandTimeout: commandTimeoutSeconds,
            cancellationToken: ct));
    }

    private static async Task<long> ExecuteCountedInsertAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string insertSql,
        string countedInsertSql,
        DynamicParameters parameters,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        if (connection is SqlConnection)
        {
            // RecordsAffected 会受 SET NOCOUNT 影响；OUTPUT INTO 精确统计 INSERT 的
            // 逻辑 inserted 行。正式表所有启用的 INSERT trigger 已在 preflight 拒绝。
            return await connection.QuerySingleAsync<long>(
                new CommandDefinition(
                    countedInsertSql,
                    parameters,
                    transaction: transaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: ct));
        }

        return await connection.ExecuteAsync(
            new CommandDefinition(
                insertSql,
                parameters,
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
    }

    private static string QuoteStage(
        Stage stage,
        RelationalDataSourceConnection.Probe targetProbe) =>
        RelationalDataSourceConnection.QuoteIdentifierPart(
            stage.Name,
            targetProbe);

    private static string QuoteRowId(
        Stage stage,
        RelationalDataSourceConnection.Probe targetProbe) =>
        RelationalDataSourceConnection.QuoteIdentifierPart(
            stage.RowIdColumn,
            targetProbe);

    private static DbType? GetDbType(
        object? value,
        RelationalDataSourceConnection.Probe targetProbe) =>
        value switch
        {
            DateTime dt when targetProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql"
                => dt.Kind == DateTimeKind.Utc ? DbType.DateTime : DbType.DateTime2,
            DateTime => DbType.DateTime2,
            DateTimeOffset => DbType.DateTimeOffset,
            DateOnly => DbType.Date,
            TimeOnly => DbType.Time,
            TimeSpan => DbType.Time,
            Guid => DbType.Guid,
            byte[] => DbType.Binary,
            _ => null,
        };

    internal static object? NormalizeParameterValue(object? value) =>
        value switch
        {
            // SqlClient 不接受 DbType.SByte/UInt16/UInt32/UInt64。统一无损拓宽，
            // 既覆盖 MySQL→SQL Server，也避免同一源值随目标 provider 改变语义。
            sbyte typed => (short)typed,
            ushort typed => (int)typed,
            uint typed => (long)typed,
            ulong typed => (decimal)typed,
            _ => value,
        };
}
