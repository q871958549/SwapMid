using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;

namespace SwapMid.DataIntegration;

/// <summary>
/// 源 reader 已关闭后的正式表短事务。DELETE 与所有 staging→target 批次共用一个事务，
/// 在提交开始前发生的少写、取消或中批异常都恢复正式表执行前快照。
/// </summary>
internal static partial class EtlTargetAtomicWriter
{
    private const int MySqlWriterLeaseTimeoutSeconds = 5;

    internal static async Task ApplyAsync(
        DbConnection connection,
        EtlTargetStagingWriter.Stage stage,
        string targetTable,
        IReadOnlyList<string> columns,
        long totalRows,
        bool truncate,
        int transferBatchSize,
        int maxExistingTargetRows,
        long maxExistingTargetAllocatedBytes,
        string expectedSchemaFingerprint,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        int cleanupTimeoutSeconds,
        ILogger logger,
        EtlTargetConnectionCleanupState connectionCleanupState,
        IEtlTargetReceiptStore receiptStore,
        EtlTargetReceiptEvidence? receiptEvidence,
        string? targetCoordinationIdentityHash,
        CancellationToken commitConfirmationToken,
        CancellationToken ct)
    {
        await EtlTargetSession.PrepareForFormalWriteAsync(
            connection,
            targetProbe,
            commandTimeoutSeconds,
            ct);
        var isolationLevel =
            truncate && connection is MySqlConnection
                ? IsolationLevel.Serializable
                : IsolationLevel.ReadCommitted;
        var targetTransaction = await connection.BeginTransactionAsync(
            isolationLevel,
            ct);
        string? mySqlWriterLease = null;
        var mySqlWriterLeaseConfirmed = false;
        var commitStarted = false;
        var commitConfirmed = false;
        var rollbackFailed = false;
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
            var existingTargetRows = await AcquireTargetGuardAsync(
                connection,
                targetTransaction,
                targetTable,
                truncate,
                truncate ? maxExistingTargetRows + 1 : 1,
                targetProbe,
                commandTimeoutSeconds,
                ct);
            var lockedSchemaFingerprint =
                await EtlTargetPreflight.RevalidateLockedAsync(
                connection,
                targetTransaction,
                targetTable,
                targetProbe,
                truncate,
                commandTimeoutSeconds,
                ct);
            if (!string.Equals(
                    expectedSchemaFingerprint,
                    lockedSchemaFingerprint,
                    StringComparison.Ordinal))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            if (truncate &&
                existingTargetRows > maxExistingTargetRows)
            {
                throw EtlPipelineRunner.InvalidInput();
            }
            if (truncate &&
                await GetTargetAllocatedBytesAsync(
                    connection,
                    targetTransaction,
                    targetTable,
                    targetProbe,
                    commandTimeoutSeconds,
                    ct) > maxExistingTargetAllocatedBytes)
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            ct.ThrowIfCancellationRequested();
            if (receiptEvidence != null)
            {
                if (string.IsNullOrWhiteSpace(
                        targetCoordinationIdentityHash))
                {
                    throw EtlPipelineRunner.InvalidInput();
                }

                var receiptResult = await receiptStore.InsertOrReadAsync(
                    connection,
                    targetTransaction,
                    new EtlTargetReceiptProviderContext(
                        targetProbe.NormalizedProvider,
                        connection.Database,
                        targetCoordinationIdentityHash),
                    receiptEvidence,
                    commandTimeoutSeconds,
                    ct);
                if (receiptResult != EtlTargetReceiptInsertResult.Inserted)
                {
                    // 已有同 RunId 无论 exact/mismatch 都不能继续触碰业务表；由上层落
                    // OutcomeUnknown 后走只读核验，绝不能记普通 Failed 放行新 RunId。
                    // mismatch 也必须保留本次已准备的 schema/batch/rows，避免后续把
                    // 目标库中的另一批次回执误认为本次执行成功。
                    throw new EtlTargetCommitOutcomeUnknownException(
                        receiptEvidence);
                }
            }

            if (truncate)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    $"DELETE FROM {RelationalDataSourceConnection.QuoteIdentifier(targetTable, targetProbe)}",
                    transaction: targetTransaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: ct));
            }

            for (long firstRowId = 1;
                 firstRowId <= totalRows;
                 firstRowId += transferBatchSize)
            {
                var expectedRows = checked((int)Math.Min(
                    transferBatchSize,
                    totalRows - firstRowId + 1));
                await EtlTargetStagingWriter.TransferBatchAsync(
                    connection,
                    targetTransaction,
                    stage,
                    targetTable,
                    columns,
                    firstRowId,
                    expectedRows,
                    targetProbe,
                    commandTimeoutSeconds,
                    ct);
            }

            ct.ThrowIfCancellationRequested();
            // 提交开始后不再用请求/执行权 token 打断，避免已提交却向调用方报告取消；但
            // 仍受 runner 独立总期限约束，黑洞连接到期统一进入 OutcomeUnknown。
            commitStarted = true;
            await targetTransaction.CommitAsync(commitConfirmationToken);
            commitConfirmed = true;
        }
        catch
        {
            try
            {
                using var cleanup = new CancellationTokenSource(
                    TimeSpan.FromSeconds(cleanupTimeoutSeconds));
                await targetTransaction.RollbackAsync(cleanup.Token);
            }
            catch (Exception rollbackException)
            {
                rollbackFailed = true;
                connectionCleanupState.RequirePhysicalSessionAbort();
                LogWarningNoThrow(
                    logger,
                    "ETL 目标事务回滚失败；将销毁目标物理会话 exceptionType={ExceptionType}",
                    rollbackException.GetType().FullName ??
                    rollbackException.GetType().Name);
            }

            if (commitStarted)
            {
                // COMMIT 已发出后，客户端异常与后续 rollback 结果都不能证明服务器未提交。
                // 统一升级为未知态，防止上层误记 Failed 后自动/人工重试造成重复写。
                throw new EtlTargetCommitOutcomeUnknownException(
                    receiptEvidence);
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
                    // Dispose 发生在 COMMIT ACK 之后时只属于清理故障，绝不能反转已确认提交。
                    // MySQL 连接由 runner 的专用 lease 销毁，不会带锁回池。
                    if (connection is MySqlConnection or NpgsqlConnection)
                    {
                        connectionCleanupState.RequirePhysicalSessionAbort();
                    }
                    LogWarningNoThrow(
                        logger,
                        "ETL 目标事务释放失败 commitConfirmed={CommitConfirmed} exceptionType={ExceptionType}",
                        commitConfirmed,
                        disposeException.GetType().FullName ??
                        disposeException.GetType().Name);
                }
            }

            if (mySqlWriterLease != null)
            {
                if (connectionCleanupState.RequiresPhysicalSessionAbort)
                {
                    LogWarningNoThrow(
                        logger,
                        "ETL MySQL 写者锁将由非池化物理会话销毁释放");
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

    private static void LogWarningNoThrow(
        ILogger logger,
        string message,
        params object?[] arguments)
    {
        try
        {
            logger.LogWarning(message, arguments);
        }
        catch
        {
            // 可观测性提供方不得改变 ETL 的目标事务语义。
        }
    }

    internal static async Task AcquireSqlServerWriterLeaseAsync(
        DbConnection connection,
        DbTransaction targetTransaction,
        string targetTable,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        if (connection is not SqlConnection)
        {
            return;
        }

        const string canonicalNameSql = """
            DECLARE @objectId int = OBJECT_ID(@targetTable, N'U');
            SELECT CASE
                WHEN @objectId IS NULL THEN NULL
                ELSE QUOTENAME(OBJECT_SCHEMA_NAME(@objectId))
                   + N'.'
                   + QUOTENAME(OBJECT_NAME(@objectId))
                END
            """;
        var canonicalTarget =
            await connection.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(
                    canonicalNameSql,
                    new { targetTable },
                    transaction: targetTransaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(canonicalTarget))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        // sp_getapplock 的 resource 最多 255 字符；使用规范表名的摘要既避免截断
        // 冲突，也把 dbo.t 与未限定 t 收敛到同一写者租约。
        var resource =
            "SwapMid:ETL:" +
            Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    canonicalTarget.ToUpperInvariant())));
        const string acquireSql = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = @lockTimeoutMilliseconds;
            SELECT @result;
            """;
        var result = await connection.QuerySingleAsync<int>(
            new CommandDefinition(
                acquireSql,
                new
                {
                    resource,
                    lockTimeoutMilliseconds =
                        checked(commandTimeoutSeconds * 1_000),
                },
                transaction: targetTransaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        if (result < 0)
        {
            throw new TimeoutException(
                "ETL target writer lease could not be acquired.");
        }
    }

    internal static async Task AcquirePostgreSqlWriterLeaseAsync(
        DbConnection connection,
        DbTransaction targetTransaction,
        string targetTable,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        if (connection is not NpgsqlConnection ||
            targetProbe.NormalizedProvider is not ("postgresql" or "postgres" or "npgsql"))
        {
            return;
        }

        var parts = targetTable.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries);
        var schema = parts.Length == 2 ? parts[0] : "public";
        var table = parts[^1];
        var canonicalTarget = string.Join(
            '\u001f',
            connection.Database?.Trim() ?? string.Empty,
            schema.Trim(),
            table.Trim()).ToUpperInvariant();
        var digest = SHA256.HashData(
            Encoding.UTF8.GetBytes("SwapMid:ETL:PG:" + canonicalTarget));
        var key1 = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(
            digest.AsSpan(0, 4));
        var key2 = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(
            digest.AsSpan(4, 4));

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_catalog.pg_advisory_xact_lock(@key1, @key2)",
            new { key1, key2 },
            transaction: targetTransaction,
            commandTimeout: commandTimeoutSeconds,
            cancellationToken: ct));
    }


    private static async Task<long> AcquireTargetGuardAsync(
        DbConnection connection,
        DbTransaction targetTransaction,
        string targetTable,
        bool truncate,
        int boundedLimit,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var quotedTarget =
            RelationalDataSourceConnection.QuoteIdentifier(
                targetTable,
                targetProbe);
        string sql;
        if (connection is SqlConnection)
        {
            var lockHints = truncate
                ? "TABLOCKX, HOLDLOCK"
                : "TABLOCK, HOLDLOCK";
            sql = $"""
               SELECT COUNT_BIG(*)
               FROM
               (
                   SELECT TOP (@boundedLimit) 1 AS [marker]
                   FROM {quotedTarget} WITH ({lockHints})
               ) AS [etl_bounded]
               """;
        }
        else
        {
            if (connection is NpgsqlConnection)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    $"LOCK TABLE {quotedTarget} IN EXCLUSIVE MODE",
                    transaction: targetTransaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: ct));
            }

            sql = $"""
               SELECT COUNT(*)
               FROM
               (
                   SELECT 1 AS marker
                   FROM {quotedTarget}
                   LIMIT @boundedLimit
               ) AS etl_bounded
               """;
        }

        return await connection.QuerySingleAsync<long>(
            new CommandDefinition(
                sql,
                new { boundedLimit },
                transaction: targetTransaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
    }

    private static async Task<long> GetTargetAllocatedBytesAsync(
        DbConnection connection,
        DbTransaction targetTransaction,
        string targetTable,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        if (connection is SqlConnection)
        {
            const string sqlServerSizeSql = """
                SELECT COALESCE(
                    SUM(CONVERT(bigint, au.total_pages)) * 8192,
                    0)
                FROM sys.partitions AS p
                INNER JOIN sys.allocation_units AS au
                    ON au.container_id =
                        CASE
                            WHEN au.type IN (1, 3) THEN p.hobt_id
                            ELSE p.partition_id
                        END
                WHERE p.object_id = OBJECT_ID(@targetTable, N'U')
                """;
            return await connection.QuerySingleAsync<long>(
                new CommandDefinition(
                    sqlServerSizeSql,
                    new { targetTable },
                    transaction: targetTransaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: ct));
        }

        if (connection is MySqlConnection)
        {
            var parts = targetTable.Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries);
            var schema = parts.Length == 2
                ? parts[0]
                : connection.Database;
            var table = parts[^1];
            const string mySqlSizeSql = """
                SELECT COALESCE(
                    DATA_LENGTH + INDEX_LENGTH + DATA_FREE,
                    0)
                FROM information_schema.TABLES
                WHERE BINARY TABLE_SCHEMA = BINARY @schema
                  AND BINARY TABLE_NAME = BINARY @table
                """;
            return await connection.QuerySingleAsync<long>(
                new CommandDefinition(
                    mySqlSizeSql,
                    new { schema, table },
                    transaction: targetTransaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: ct));
        }

        if (connection is NpgsqlConnection)
        {
            var parts = targetTable.Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries);
            var schema = parts.Length == 2 ? parts[0] : "public";
            var table = parts[^1];
            const string pgSizeSql = """
                SELECT COALESCE(pg_catalog.pg_total_relation_size(c.oid), 0)
                FROM pg_catalog.pg_class AS c
                INNER JOIN pg_catalog.pg_namespace AS n
                    ON n.oid = c.relnamespace
                WHERE n.nspname = @schema
                  AND c.relname = @table
                  AND c.relkind = 'r'
                """;
            return await connection.QuerySingleAsync<long>(
                new CommandDefinition(
                    pgSizeSql,
                    new { schema, table },
                    transaction: targetTransaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: ct));
        }

        // SQLite/recording fake 只用于事务语义测试；生产 provider 不会进入此分支。
        return 0;
    }
}
