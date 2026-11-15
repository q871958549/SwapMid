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
/// Upsert 暂存去重守卫与方言原子转存（PostgreSQL <c>ON CONFLICT DO UPDATE</c>、
/// SQL Server <c>MERGE WITH (HOLDLOCK)</c>、MySQL <c>ON DUPLICATE KEY UPDATE</c>）。
/// </summary>
internal static class EtlTargetUpsertWriter
{
    /// <summary>
    /// 在开启正式表事务前核验暂存表内是否存在 NULL 键或同批次重复键，避免单语句
    /// <c>ON CONFLICT DO UPDATE</c> / <c>MERGE</c> 命中同一目标行两次或绕过唯一键。
    /// </summary>
    internal static async Task VerifyStageKeysAsync(
        DbConnection connection,
        EtlTargetStagingWriter.Stage stage,
        EtlTargetUpsertPreflight.ResolvedUpsertPlan plan,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var quotedStage = RelationalDataSourceConnection.QuoteIdentifierPart(
            stage.Name,
            targetProbe);
        var quotedKeys = plan.KeyColumns
            .Select(key => RelationalDataSourceConnection.QuoteIdentifierPart(key, targetProbe))
            .ToArray();
        var nullPredicate = string.Join(
            " OR ",
            quotedKeys.Select(key => $"{key} IS NULL"));
        var groupByKeys = string.Join(", ", quotedKeys);

        string checkSql;
        if (connection is SqlConnection)
        {
            checkSql = $"""
                SELECT CASE
                    WHEN EXISTS (
                        SELECT TOP (1) 1
                        FROM {quotedStage}
                        WHERE {nullPredicate}
                    ) THEN 1
                    WHEN EXISTS (
                        SELECT TOP (1) 1
                        FROM {quotedStage}
                        GROUP BY {groupByKeys}
                        HAVING COUNT_BIG(*) > 1
                    ) THEN 1
                    ELSE 0
                END
                """;
        }
        else
        {
            checkSql = $"""
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM {quotedStage}
                        WHERE {nullPredicate}
                        LIMIT 1
                    ) THEN 1
                    WHEN EXISTS (
                        SELECT 1
                        FROM {quotedStage}
                        GROUP BY {groupByKeys}
                        HAVING COUNT(*) > 1
                        LIMIT 1
                    ) THEN 1
                    ELSE 0
                END
                """;
        }

        var hasInvalidOrDuplicateKeys = await connection.QuerySingleAsync<int>(
            new CommandDefinition(
                checkSql,
                transaction: null,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        if (hasInvalidOrDuplicateKeys != 0)
        {
            throw EtlPipelineRunner.InvalidInput();
        }
    }

    internal static async Task TransferBatchAsync(
        DbConnection connection,
        DbTransaction targetTransaction,
        EtlTargetStagingWriter.Stage stage,
        string targetTable,
        IReadOnlyList<string> columns,
        EtlTargetUpsertPreflight.ResolvedUpsertPlan plan,
        long firstRowId,
        int expectedRows,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var quotedTarget = RelationalDataSourceConnection.QuoteIdentifier(
            targetTable,
            targetProbe);
        var quotedStage = RelationalDataSourceConnection.QuoteIdentifierPart(
            stage.Name,
            targetProbe);
        var quotedRowId = RelationalDataSourceConnection.QuoteIdentifierPart(
            stage.RowIdColumn,
            targetProbe);
        var quotedColumns = string.Join(
            ", ",
            columns.Select(col =>
                RelationalDataSourceConnection.QuoteIdentifierPart(col, targetProbe)));
        var quotedKeys = string.Join(
            ", ",
            plan.KeyColumns.Select(col =>
                RelationalDataSourceConnection.QuoteIdentifierPart(col, targetProbe)));
        var quotedWatermark = plan.WatermarkColumn == null
            ? null
            : RelationalDataSourceConnection.QuoteIdentifierPart(
                plan.WatermarkColumn,
                targetProbe);

        var parameters = new DynamicParameters();
        parameters.Add("stageStartRowId", firstRowId);
        parameters.Add("stageEndRowId", checked(firstRowId + expectedRows));

        if (connection is SqlConnection)
        {
            var joinPredicate = string.Join(
                " AND ",
                plan.KeyColumns.Select(col =>
                {
                    var q = RelationalDataSourceConnection.QuoteIdentifierPart(col, targetProbe);
                    return $"[t].{q} = [s].{q}";
                }));
            var updateSet = string.Join(
                ", ",
                plan.UpdateColumns.Select(col =>
                {
                    var q = RelationalDataSourceConnection.QuoteIdentifierPart(col, targetProbe);
                    return $"[t].{q} = [s].{q}";
                }));
            var sourceValues = string.Join(
                ", ",
                columns.Select(col =>
                    $"[s].{RelationalDataSourceConnection.QuoteIdentifierPart(col, targetProbe)}"));
            var watermarkClause = quotedWatermark == null
                ? string.Empty
                : $"AND ([t].{quotedWatermark} IS NULL OR [s].{quotedWatermark} >= [t].{quotedWatermark})";

            var mergeSql = $"""
                SET NOCOUNT ON;
                DECLARE @etl_merged TABLE ([action_kind] nvarchar(10) NOT NULL);
                MERGE {quotedTarget} WITH (HOLDLOCK) AS [t]
                USING (
                    SELECT {quotedColumns}
                    FROM {quotedStage}
                    WHERE {quotedRowId} >= @stageStartRowId
                      AND {quotedRowId} < @stageEndRowId
                ) AS [s]
                ON ({joinPredicate})
                WHEN MATCHED {watermarkClause} THEN
                    UPDATE SET {updateSet}
                WHEN NOT MATCHED BY TARGET THEN
                    INSERT ({quotedColumns})
                    VALUES ({sourceValues})
                OUTPUT $action INTO @etl_merged;
                SELECT COUNT_BIG(*) FROM @etl_merged;
                """;
            var mergedRows = await connection.QuerySingleAsync<long>(
                new CommandDefinition(
                    mergeSql,
                    parameters,
                    transaction: targetTransaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: ct));
            ValidateAffectedCount(mergedRows, expectedRows, hasWatermark: quotedWatermark != null);
            return;
        }

        if (connection is MySqlConnection &&
            targetProbe.NormalizedProvider is "mysql" or "mariadb")
        {
            var srcColumns = string.Join(
                ", ",
                columns.Select(col =>
                    $"`etl_src`.{RelationalDataSourceConnection.QuoteIdentifierPart(col, targetProbe)}"));
            var updateAssignments = string.Join(
                ", ",
                plan.UpdateColumns.Select(col =>
                {
                    var q = RelationalDataSourceConnection.QuoteIdentifierPart(col, targetProbe);
                    return quotedWatermark == null
                        ? $"{q} = `etl_src`.{q}"
                        : $"{q} = IF({quotedTarget}.{quotedWatermark} IS NULL OR `etl_src`.{quotedWatermark} >= {quotedTarget}.{quotedWatermark}, `etl_src`.{q}, {quotedTarget}.{q})";
                }));
            var mySqlUpsert = $"""
                INSERT INTO {quotedTarget} ({quotedColumns})
                SELECT {srcColumns}
                FROM {quotedStage} AS `etl_src`
                WHERE `etl_src`.{quotedRowId} >= @stageStartRowId
                  AND `etl_src`.{quotedRowId} < @stageEndRowId
                ON DUPLICATE KEY UPDATE {updateAssignments}
                """;
            var affected = await connection.ExecuteAsync(
                new CommandDefinition(
                    mySqlUpsert,
                    parameters,
                    transaction: targetTransaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: ct));
            if (affected < 0 || affected > expectedRows * 2L)
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            return;
        }

        // PostgreSQL 9.5+ 与 SQLite 3.24+（单元测试桩）均原生支持 INSERT ... SELECT ... WHERE ... ON CONFLICT DO UPDATE
        var isPostgreSql = connection is NpgsqlConnection &&
            targetProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql";
        var targetAliasClause = isPostgreSql ? " AS \"etl_target\"" : string.Empty;
        var targetColumnQualifier = isPostgreSql
            ? "\"etl_target\""
            : RelationalDataSourceConnection.QuoteIdentifierPart(
                targetTable.Split('.', StringSplitOptions.RemoveEmptyEntries)[^1],
                targetProbe);
        var pgUpdateSet = string.Join(
            ", ",
            plan.UpdateColumns.Select(col =>
            {
                var q = RelationalDataSourceConnection.QuoteIdentifierPart(col, targetProbe);
                return $"{q} = EXCLUDED.{q}";
            }));
        var pgWatermarkWhere = quotedWatermark == null
            ? string.Empty
            : $"WHERE {targetColumnQualifier}.{quotedWatermark} IS NULL OR EXCLUDED.{quotedWatermark} >= {targetColumnQualifier}.{quotedWatermark}";
        var pgUpsertSql = $"""
            INSERT INTO {quotedTarget}{targetAliasClause} ({quotedColumns})
            SELECT {quotedColumns}
            FROM {quotedStage}
            WHERE {quotedRowId} >= @stageStartRowId
              AND {quotedRowId} < @stageEndRowId
            ON CONFLICT ({quotedKeys})
            DO UPDATE SET {pgUpdateSet}
            {pgWatermarkWhere}
            """;
        var pgAffected = await connection.ExecuteAsync(
            new CommandDefinition(
                pgUpsertSql,
                parameters,
                transaction: targetTransaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        ValidateAffectedCount(pgAffected, expectedRows, hasWatermark: quotedWatermark != null);
    }

    private static void ValidateAffectedCount(
        long affectedRows,
        int expectedRows,
        bool hasWatermark)
    {
        if (hasWatermark)
        {
            if (affectedRows < 0 || affectedRows > expectedRows)
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            return;
        }

        if (affectedRows != expectedRows)
        {
            throw EtlPipelineRunner.InvalidInput();
        }
    }
}
