using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace SwapMid.DataIntegration;

public partial class EtlPipelineRunner
{
    private void LogPostCommitCleanupFailureNoThrow(string exceptionType)
    {
        try
        {
            _logger.LogWarning(
                "ETL 目标提交已确认，后置清理或可观测性操作失败 exceptionType={ExceptionType}",
                exceptionType);
        }
        catch
        {
            // 可观测性提供方不得改变已确认的目标提交结果。
        }
    }

    private static async Task<DbTransaction?> BeginSourceReadOnlyTransactionAsync(
        DbConnection connection,
        RelationalDataSourceConnection.Probe probe,
        CancellationToken ct)
    {
        if (probe.NormalizedProvider is "sqlserver" or "mssql" &&
            connection is Microsoft.Data.SqlClient.SqlConnection sqlServer)
        {
            // 流式 READ COMMITTED 会逐步释放 S 锁，长查询可能混入多个时间点的数据。
            // 显式 SNAPSHOT 固定整条 SELECT；源库未启用 ALLOW_SNAPSHOT_ISOLATION
            // 时由数据库拒绝，不能静默降级成非一致全量结果。
            return await sqlServer.BeginTransactionAsync(
                IsolationLevel.Snapshot,
                ct);
        }

        if (probe.NormalizedProvider is "mysql" or "mariadb" &&
            connection is MySqlConnector.MySqlConnection mysql)
        {
            return await mysql.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                isReadOnly: true,
                cancellationToken: ct);
        }

        if (probe.NormalizedProvider is "postgresql" or "postgres" or "npgsql" &&
            connection is Npgsql.NpgsqlConnection postgres)
        {
            var transaction = await postgres.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                ct);
            try
            {
                await using var command = postgres.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                command.CommandText = "SET TRANSACTION READ ONLY";
                await command.ExecuteNonQueryAsync(ct);
                return transaction;
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }
        }

        return null;
    }

    internal readonly record struct EtlColumnProjection(
        string[] TargetColumns,
        int[]? SourceOrdinals,
        int SourceFieldCount)
    {
        internal object?[] ProjectRow(object?[] sourceValues)
        {
            if (SourceOrdinals == null)
            {
                return sourceValues;
            }

            var projected = new object?[TargetColumns.Length];
            for (var i = 0; i < SourceOrdinals.Length; i++)
            {
                projected[i] = sourceValues[SourceOrdinals[i]];
            }

            return projected;
        }
    }

    private static EtlColumnProjection ResolveColumnProjection(
        DbDataReader reader,
        EtlTransformConfig cfg,
        RelationalDataSourceConnection.Probe targetProbe)
    {
        var sourceColumns = GetValidatedSourceColumns(reader);
        var maxIdentifierLength = targetProbe.NormalizedProvider switch
        {
            "mysql" or "mariadb" => 64,
            "postgresql" or "postgres" or "npgsql" => 63,
            _ => 128,
        };

        if (cfg.ColumnMappings.Count == 0)
        {
            foreach (var column in sourceColumns)
            {
                if (column.Length > maxIdentifierLength)
                {
                    throw InvalidInput();
                }
            }

            return new EtlColumnProjection(
                sourceColumns,
                SourceOrdinals: null,
                SourceFieldCount: reader.FieldCount);
        }

        if (cfg.ColumnMappings.Count > MaxColumns)
        {
            throw InvalidInput();
        }

        var sourceOrdinalsByName = new Dictionary<string, int>(
            sourceColumns.Length,
            StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sourceColumns.Length; i++)
        {
            sourceOrdinalsByName[sourceColumns[i]] = i;
        }

        var targetColumns = new string[cfg.ColumnMappings.Count];
        var sourceOrdinals = new int[cfg.ColumnMappings.Count];
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cfg.ColumnMappings.Count; i++)
        {
            var mapping = cfg.ColumnMappings[i];
            if (!sourceOrdinalsByName.TryGetValue(mapping.SourceColumn, out var ordinal) ||
                mapping.TargetColumn.Length > maxIdentifierLength ||
                mapping.TargetColumn.Any(char.IsControl) ||
                !seenTargets.Add(mapping.TargetColumn))
            {
                throw InvalidInput();
            }

            targetColumns[i] = mapping.TargetColumn;
            sourceOrdinals[i] = ordinal;
        }

        return new EtlColumnProjection(
            targetColumns,
            sourceOrdinals,
            SourceFieldCount: reader.FieldCount);
    }

    private static string[] GetValidatedSourceColumns(DbDataReader reader)
    {
        if (reader.FieldCount is < 1 or > MaxColumns)
        {
            throw InvalidInput();
        }

        var columns = new string[reader.FieldCount];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var column = reader.GetName(i);
            if (string.IsNullOrWhiteSpace(column) ||
                column.Any(char.IsControl) ||
                !seen.Add(column))
            {
                throw InvalidInput();
            }

            columns[i] = column;
        }

        return columns;
    }

    private static int GetMaxRowsPerBatch(
        int columnCount,
        RelationalDataSourceConnection.Probe targetProbe)
    {
        // 每个 staging 行额外携带一个内部序号参数。
        var parametersPerRow = checked(columnCount + 1);
        if (targetProbe.NormalizedProvider is "mysql" or "mariadb" or "postgresql" or "postgres" or "npgsql")
        {
            return Math.Max(
                1,
                Math.Min(
                    BatchSize,
                    MySqlBatchParameterLimit / parametersPerRow));
        }

        return Math.Max(
            1,
            Math.Min(
                BatchSize,
                SqlServerBatchParameterLimit / parametersPerRow));
    }

    internal static long GetMaxRowsForShape(
        int columnCount,
        int maxRowsPerBatch)
    {
        if (columnCount <= 0 || maxRowsPerBatch <= 0)
        {
            throw InvalidInput();
        }

        return Math.Min(
            MaxRows,
            Math.Min(
                MaxTotalCells / columnCount,
                checked((long)MaxStagingBatches * maxRowsPerBatch)));
    }

    private static bool IsSupportedProvider(RelationalDataSourceConnection.Probe probe)
        => probe.NormalizedProvider is
            "sqlserver" or "mssql" or
            "mysql" or "mariadb" or
            "postgresql" or "postgres" or "npgsql";

    internal static BusinessException InvalidInput()
        => new(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
}
