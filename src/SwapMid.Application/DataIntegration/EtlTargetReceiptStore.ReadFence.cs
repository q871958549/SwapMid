using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SwapMid.DataIntegration;

public sealed partial class EtlTargetReceiptStore
{
    public async Task<IEtlTargetReceiptReadFence> AcquireReadFenceAsync(
        DataSource target,
        EtlExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (executionContext.TargetReceipt == null ||
            string.IsNullOrWhiteSpace(
                executionContext.TargetCoordinationIdentityHash))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var probe = ParseSupportedTarget(target.Config);
        EnsureReceiptWriteModeSupported(
            probe,
            executionContext.WriteMode);
        var connection = _connectionFactory.CreateTarget(
            target.Config,
            ConnectTimeoutSeconds,
            CommandTimeoutSeconds);
        DbTransaction? transaction = null;
        string? mySqlWriterLease = null;
        var mySqlWriterLeaseConfirmed = false;
        try
        {
            await connection.OpenAsync(cancellationToken);
            await EtlProviderSession.ConfigureAsync(
                connection,
                probe,
                CommandTimeoutSeconds,
                cancellationToken);
            transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            await EtlTargetAtomicWriter.AcquireSqlServerWriterLeaseAsync(
                connection,
                transaction,
                executionContext.CanonicalTargetTable,
                CommandTimeoutSeconds,
                cancellationToken);
            await EtlTargetAtomicWriter.AcquirePostgreSqlWriterLeaseAsync(
                connection,
                transaction,
                executionContext.CanonicalTargetTable,
                probe,
                CommandTimeoutSeconds,
                cancellationToken);
            mySqlWriterLease =
                EtlTargetAtomicWriter.BuildMySqlWriterLeaseResource(
                    connection,
                    executionContext.CanonicalTargetTable,
                    probe);
            if (mySqlWriterLease != null)
            {
                await EtlTargetAtomicWriter.AcquireMySqlWriterLeaseAsync(
                    connection,
                    transaction,
                    mySqlWriterLease,
                    CommandTimeoutSeconds,
                    cancellationToken);
                mySqlWriterLeaseConfirmed = true;
            }

            await EnsureCoordinationIdentityAsync(
                connection,
                transaction,
                probe,
                executionContext.TargetCoordinationIdentityHash,
                CommandTimeoutSeconds,
                cancellationToken);
            await EnsureContractShapeAsync(
                connection,
                transaction,
                probe,
                CommandTimeoutSeconds,
                cancellationToken);
            var instanceId = await ReadResourceInstanceIdAsync(
                connection,
                transaction,
                probe,
                CommandTimeoutSeconds,
                cancellationToken);
            if (instanceId !=
                executionContext.TargetReceipt.ResourceInstanceId)
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            var canonicalTarget =
                await EtlTargetSession.ResolveCanonicalTargetTableAsync(
                    connection,
                     executionContext.CanonicalTargetTable,
                     probe,
                     CommandTimeoutSeconds,
                     cancellationToken,
                     transaction);
            EnsureBusinessTargetIsNotReserved(
                canonicalTarget,
                probe,
                connection.Database);
            var provider = NormalizeProvider(probe.NormalizedProvider);
            if (!string.Equals(
                    canonicalTarget,
                    executionContext.CanonicalTargetTable,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    EtlReceiptHashes.ComputeTargetResource(
                        provider,
                        instanceId,
                        canonicalTarget),
                    executionContext.VerifiedTargetResourceHash,
                    StringComparison.Ordinal))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            var receipt = await ReadReceiptAsync(
                connection,
                transaction,
                probe,
                executionContext.RunId,
                forUpdate: false,
                CommandTimeoutSeconds,
                cancellationToken);
            return new TargetReceiptReadFence(
                connection,
                transaction,
                mySqlWriterLease,
                mySqlWriterLeaseConfirmed,
                _logger,
                receipt);
        }
        catch
        {
            await DisposeReadFenceResourcesNoThrowAsync(
                connection,
                transaction,
                mySqlWriterLease,
                mySqlWriterLeaseConfirmed,
                _logger);
            throw;
        }
    }

    private static async Task DisposeReadFenceResourcesNoThrowAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string? mySqlWriterLease,
        bool mySqlWriterLeaseConfirmed,
        ILogger logger)
    {
        if (transaction != null)
        {
            try
            {
                using var cleanup = new CancellationTokenSource(
                    TimeSpan.FromSeconds(CleanupTimeoutSeconds));
                await transaction.RollbackAsync(cleanup.Token);
            }
            catch (Exception exception)
            {
                // 只读事务清理失败时仍销毁物理连接；不得把失败会话返回继续使用。
                LogReadFenceCleanupWarningNoThrow(
                    logger,
                    "rollback",
                    exception.GetType().FullName ?? exception.GetType().Name);
            }
        }

        if (mySqlWriterLease != null)
        {
            await EtlTargetAtomicWriter.ReleaseMySqlWriterLeaseNoThrowAsync(
                connection,
                mySqlWriterLease,
                mySqlWriterLeaseConfirmed,
                CleanupTimeoutSeconds,
                logger);
        }

        if (transaction != null)
        {
            try
            {
                await transaction.DisposeAsync();
            }
            catch (Exception exception)
            {
                // 继续销毁连接，确保目标侧锁最终随 session 退出。
                LogReadFenceCleanupWarningNoThrow(
                    logger,
                    "transaction_dispose",
                    exception.GetType().FullName ?? exception.GetType().Name);
            }
        }

        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception exception)
        {
            // 清理故障不得覆盖已确定的平台状态；连接工厂的非池化 MySQL
            // profile 与 SqlClient broken-session 处理负责最终关闭 socket。
            LogReadFenceCleanupWarningNoThrow(
                logger,
                "connection_dispose",
                exception.GetType().FullName ?? exception.GetType().Name);
        }
    }

    private static void LogReadFenceCleanupWarningNoThrow(
        ILogger logger,
        string phase,
        string exceptionType)
    {
        try
        {
            logger.LogWarning(
                "ETL 目标回执读栅栏清理失败 phase={Phase} exceptionType={ExceptionType}",
                phase,
                exceptionType);
        }
        catch
        {
            // 日志提供方不得覆盖已确定的平台状态或改变目标锁释放语义。
        }
    }

    private sealed class TargetReceiptReadFence :
        IEtlTargetReceiptReadFence
    {
        private readonly DbConnection _connection;
        private readonly DbTransaction _transaction;
        private readonly string? _mySqlWriterLease;
        private readonly bool _mySqlWriterLeaseConfirmed;
        private readonly ILogger _logger;
        private int _disposed;

        internal TargetReceiptReadFence(
            DbConnection connection,
            DbTransaction transaction,
            string? mySqlWriterLease,
            bool mySqlWriterLeaseConfirmed,
            ILogger logger,
            EtlTargetReceiptEvidence? receipt)
        {
            _connection = connection;
            _transaction = transaction;
            _mySqlWriterLease = mySqlWriterLease;
            _mySqlWriterLeaseConfirmed = mySqlWriterLeaseConfirmed;
            _logger = logger;
            Receipt = receipt;
        }

        public EtlTargetReceiptEvidence? Receipt { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            await DisposeReadFenceResourcesNoThrowAsync(
                _connection,
                _transaction,
                _mySqlWriterLease,
                _mySqlWriterLeaseConfirmed,
                _logger);
        }
    }
}
