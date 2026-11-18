using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;

namespace SwapMid.DataIntegration;

/// <summary>
/// <see cref="EtlTargetAtomicWriter"/> 的 CDC 关系型目标表行级镜像事务扩展：
/// 在同一短事务与写者租约保护下原子应用暂存 Upsert 与净删除（Delete）集合。
/// </summary>
internal static partial class EtlTargetAtomicWriter
{
    private const int DeleteChunkSize = 100;

    internal sealed record CdcMirrorSoftDeleteSpec(
        string FlagColumn,
        string? TimestampColumn);

    internal sealed record CdcMirrorSoftDeleteTuple(
        object?[] KeyValues,
        object FlagValue,
        object? TimestampValue);

    internal static async Task<(long Upserted, long Deleted)> ApplyCdcMirrorAsync(
        DbConnection connection,
        EtlTargetStagingWriter.Stage? stage,
        string targetTable,
        IReadOnlyList<string> upsertColumns,
        long totalUpserts,
        EtlTargetUpsertPreflight.ResolvedUpsertPlan? upsertPlan,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<object?[]> deleteKeys,
        int transferBatchSize,
        string expectedSchemaFingerprint,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        int cleanupTimeoutSeconds,
        ILogger logger,
        EtlTargetConnectionCleanupState connectionCleanupState,
        CancellationToken ct,
        CdcMirrorSoftDeleteSpec? softDeleteSpec = null,
        IReadOnlyList<CdcMirrorSoftDeleteTuple>? softDeleteTuples = null)
    {
        if (totalUpserts <= 0 && deleteKeys.Count == 0 && (softDeleteTuples == null || softDeleteTuples.Count == 0))
        {
            return (0, 0);
        }

        await EtlTargetSession.PrepareForFormalWriteAsync(
            connection,
            targetProbe,
            commandTimeoutSeconds,
            ct);

        var targetTransaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        string? mySqlWriterLease = null;
        var mySqlWriterLeaseConfirmed = false;
        var rollbackFailed = false;
        var commitConfirmed = false;

        try
        {
            await AcquireSqlServerWriterLeaseAsync(
                connection,
                targetTransaction,
                targetTable,
                commandTimeoutSeconds,
                ct);
            await AcquirePostgreSqlWriterLeaseAsync(
                connection,
                targetTransaction,
                targetTable,
                targetProbe,
                commandTimeoutSeconds,
                ct);
            mySqlWriterLease = BuildMySqlWriterLeaseResource(
                connection,
                targetTable,
                targetProbe);
            if (mySqlWriterLease != null)
            {
                await AcquireMySqlWriterLeaseAsync(
                    connection,
                    targetTransaction,
                    mySqlWriterLease,
                    commandTimeoutSeconds,
                    ct);
                mySqlWriterLeaseConfirmed = true;
            }

            _ = await AcquireTargetGuardAsync(
                connection,
                targetTransaction,
                targetTable,
                truncate: false,
                boundedLimit: 1,
                targetProbe,
                commandTimeoutSeconds,
                ct);

            var upsertSpecForLock = upsertPlan == null
                ? null
                : new EtlUpsertSpec(upsertPlan.KeyColumns, upsertPlan.WatermarkColumn);
            var lockedSchemaFingerprint = await EtlTargetPreflight.RevalidateLockedAsync(
                connection,
                targetTransaction,
                targetTable,
                targetProbe,
                truncate: false,
                commandTimeoutSeconds,
                ct,
                upsertSpecForLock);
            if (!string.Equals(expectedSchemaFingerprint, lockedSchemaFingerprint, StringComparison.Ordinal))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            if (stage != null && totalUpserts > 0 && upsertPlan != null)
            {
                for (long firstRowId = 1; firstRowId <= totalUpserts; firstRowId += transferBatchSize)
                {
                    var expectedRows = checked((int)Math.Min(transferBatchSize, totalUpserts - firstRowId + 1));
                    await EtlTargetUpsertWriter.TransferBatchAsync(
                        connection,
                        targetTransaction,
                        stage,
                        targetTable,
                        upsertColumns,
                        upsertPlan,
                        firstRowId,
                        expectedRows,
                        targetProbe,
                        commandTimeoutSeconds,
                        ct);
                }
            }

            long deletedRows = 0;
            if (deleteKeys.Count > 0)
            {
                deletedRows += await ExecuteDeleteChunksAsync(
                    connection,
                    targetTransaction,
                    targetTable,
                    keyColumns,
                    deleteKeys,
                    targetProbe,
                    commandTimeoutSeconds,
                    ct);
            }

            if (softDeleteSpec != null && softDeleteTuples is { Count: > 0 })
            {
                deletedRows += await ExecuteSoftDeleteChunksAsync(
                    connection,
                    targetTransaction,
                    targetTable,
                    keyColumns,
                    softDeleteSpec,
                    softDeleteTuples,
                    targetProbe,
                    commandTimeoutSeconds,
                    ct);
            }

            ct.ThrowIfCancellationRequested();
            await targetTransaction.CommitAsync(ct);
            commitConfirmed = true;
            return (totalUpserts, deletedRows);
        }
        catch
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(cleanupTimeoutSeconds));
                await targetTransaction.RollbackAsync(cleanup.Token);
            }
            catch (Exception rollbackException)
            {
                rollbackFailed = true;
                connectionCleanupState.RequirePhysicalSessionAbort();
                LogWarningNoThrow(
                    logger,
                    "CDC 关系表镜像事务回滚失败；将销毁目标物理会话 exceptionType={ExceptionType}",
                    rollbackException.GetType().FullName ?? rollbackException.GetType().Name);
            }

            throw;
        }
        finally
        {
            if (!rollbackFailed)
            {
                try
                {
                    await targetTransaction.DisposeAsync();
                }
                catch (Exception disposeException)
                {
                    if (connection is MySqlConnection or NpgsqlConnection)
                    {
                        connectionCleanupState.RequirePhysicalSessionAbort();
                    }

                    LogWarningNoThrow(
                        logger,
                        "CDC 关系表镜像事务释放失败 commitConfirmed={CommitConfirmed} exceptionType={ExceptionType}",
                        commitConfirmed,
                        disposeException.GetType().FullName ?? disposeException.GetType().Name);
                }
            }

            if (mySqlWriterLease != null)
            {
                if (connectionCleanupState.RequiresPhysicalSessionAbort)
                {
                    LogWarningNoThrow(logger, "CDC 关系表镜像 MySQL 写者锁将由非池化物理会话销毁释放");
                }
                else if (!await ReleaseMySqlWriterLeaseNoThrowAsync(
                             connection,
                             mySqlWriterLease,
                             mySqlWriterLeaseConfirmed,
                             cleanupTimeoutSeconds,
                             logger))
                {
                    connectionCleanupState.RequirePhysicalSessionAbort();
                }
            }
        }
    }

    private static async Task<long> ExecuteDeleteChunksAsync(
        DbConnection connection,
        DbTransaction transaction,
        string targetTable,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<object?[]> deleteKeys,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var quotedTarget = RelationalDataSourceConnection.QuoteIdentifier(targetTable, targetProbe);
        var quotedKeys = keyColumns
            .Select(col => RelationalDataSourceConnection.QuoteIdentifierPart(col, targetProbe))
            .ToArray();

        long totalDeleted = 0;
        for (var offset = 0; offset < deleteKeys.Count; offset += DeleteChunkSize)
        {
            var chunkCount = Math.Min(DeleteChunkSize, deleteKeys.Count - offset);
            var parameters = new DynamicParameters();
            var predicates = new string[chunkCount];

            for (var i = 0; i < chunkCount; i++)
            {
                var rowKeys = deleteKeys[offset + i];
                var terms = new string[quotedKeys.Length];
                for (var k = 0; k < quotedKeys.Length; k++)
                {
                    var paramName = $"d{i}_{k}";
                    terms[k] = $"{quotedKeys[k]} = @{paramName}";
                    parameters.Add(paramName, EtlTargetStagingWriter.NormalizeParameterValue(rowKeys[k]));
                }

                predicates[i] = $"({string.Join(" AND ", terms)})";
            }

            var deleteSql = $"DELETE FROM {quotedTarget} WHERE {string.Join(" OR ", predicates)}";
            var affected = await connection.ExecuteAsync(
                new CommandDefinition(
                    deleteSql,
                    parameters,
                    transaction: transaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: ct));
            if (affected > 0)
            {
                totalDeleted += affected;
            }
        }

        return totalDeleted;
    }

    private static async Task<long> ExecuteSoftDeleteChunksAsync(
        DbConnection connection,
        DbTransaction transaction,
        string targetTable,
        IReadOnlyList<string> keyColumns,
        CdcMirrorSoftDeleteSpec spec,
        IReadOnlyList<CdcMirrorSoftDeleteTuple> softDeleteTuples,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var quotedTarget = RelationalDataSourceConnection.QuoteIdentifier(targetTable, targetProbe);
        var quotedFlag = RelationalDataSourceConnection.QuoteIdentifierPart(spec.FlagColumn, targetProbe);
        var hasTs = !string.IsNullOrWhiteSpace(spec.TimestampColumn);
        var quotedTs = hasTs
            ? RelationalDataSourceConnection.QuoteIdentifierPart(spec.TimestampColumn!, targetProbe)
            : null;
        var quotedKeys = keyColumns
            .Select(col => RelationalDataSourceConnection.QuoteIdentifierPart(col, targetProbe))
            .ToArray();

        long totalUpdated = 0;
        var byTimestamp = softDeleteTuples.GroupBy(t => t.TimestampValue);
        foreach (var group in byTimestamp)
        {
            var items = group.ToList();
            for (var offset = 0; offset < items.Count; offset += DeleteChunkSize)
            {
                var chunkCount = Math.Min(DeleteChunkSize, items.Count - offset);
                var parameters = new DynamicParameters();
                parameters.Add("sd_flag", EtlTargetStagingWriter.NormalizeParameterValue(items[offset].FlagValue));
                var setClause = $"{quotedFlag} = @sd_flag";
                if (hasTs)
                {
                    parameters.Add("sd_ts", EtlTargetStagingWriter.NormalizeParameterValue(group.Key));
                    setClause += $", {quotedTs} = @sd_ts";
                }

                var predicates = new string[chunkCount];
                for (var i = 0; i < chunkCount; i++)
                {
                    var rowKeys = items[offset + i].KeyValues;
                    var terms = new string[quotedKeys.Length];
                    for (var k = 0; k < quotedKeys.Length; k++)
                    {
                        var paramName = $"sd_k{i}_{k}";
                        terms[k] = $"{quotedKeys[k]} = @{paramName}";
                        parameters.Add(paramName, EtlTargetStagingWriter.NormalizeParameterValue(rowKeys[k]));
                    }

                    predicates[i] = $"({string.Join(" AND ", terms)})";
                }

                var updateSql = $"UPDATE {quotedTarget} SET {setClause} WHERE {string.Join(" OR ", predicates)}";
                var affected = await connection.ExecuteAsync(
                    new CommandDefinition(
                        updateSql,
                        parameters,
                        transaction: transaction,
                        commandTimeout: commandTimeoutSeconds,
                        cancellationToken: ct));
                if (affected > 0)
                {
                    totalUpdated += affected;
                }
            }
        }

        return totalUpdated;
    }
}
