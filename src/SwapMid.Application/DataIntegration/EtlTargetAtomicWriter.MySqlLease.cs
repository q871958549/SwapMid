using System;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace SwapMid.DataIntegration;

internal static partial class EtlTargetAtomicWriter
{
    internal static string? BuildMySqlWriterLeaseResource(
        DbConnection connection,
        string targetTable,
        RelationalDataSourceConnection.Probe targetProbe)
    {
        if (connection is not MySqlConnection)
        {
            return null;
        }

        // 锁名上限 64 字节，固定前缀加 55 位 SHA-256 十六进制摘要恰好为 63 字节。
        var canonicalTarget = string.Join(
            '\u001f',
            targetProbe.Database?.Trim() ?? string.Empty,
            targetTable.Trim()).ToUpperInvariant();
        var digest = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonicalTarget)));
        return "swapmid_etl_" + digest[..55];
    }

    internal static async Task AcquireMySqlWriterLeaseAsync(
        DbConnection connection,
        DbTransaction targetTransaction,
        string resource,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        // MySQL 的行/表锁不能覆盖 append 的空表，也不能跨两个独立 ETL 会话形成稳定写者锁。
        // GET_LOCK 属于服务器会话，主机名/IP 别名只要落到同一实例就会竞争同一个名字。
        // 该锁是资源组配置错误时的纵深防线，不应长期占用全局 ETL 容量槽。
        var timeoutSeconds = Math.Max(
            1,
            Math.Min(commandTimeoutSeconds, MySqlWriterLeaseTimeoutSeconds));
        var acquired = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                "SELECT GET_LOCK(@resource, @timeoutSeconds)",
                new { resource, timeoutSeconds },
                transaction: targetTransaction,
                commandTimeout: timeoutSeconds + 1,
                cancellationToken: ct));
        if (acquired != 1)
        {
            throw new TimeoutException(
                "ETL target writer lease could not be acquired.");
        }
    }

    internal static async Task<bool> ReleaseMySqlWriterLeaseNoThrowAsync(
        DbConnection connection,
        string resource,
        bool acquiredConfirmed,
        int cleanupTimeoutSeconds,
        ILogger logger)
    {
        var releaseConfirmed = false;
        try
        {
            using var cleanup = new CancellationTokenSource(
                TimeSpan.FromSeconds(cleanupTimeoutSeconds));
            var released = await connection.QuerySingleOrDefaultAsync<long?>(
                new CommandDefinition(
                    "SELECT RELEASE_LOCK(@resource)",
                    new { resource },
                    commandTimeout: cleanupTimeoutSeconds,
                    cancellationToken: cleanup.Token));
            releaseConfirmed = released == 1 ||
                (!acquiredConfirmed && released == 0);
            if (!releaseConfirmed)
            {
                LogWarningNoThrow(
                    logger,
                    "ETL MySQL 目标写者锁释放结果异常 result={Result}",
                    released);
            }
        }
        catch (Exception releaseException)
        {
            // 清理异常不能反转已确认的目标事务结果；调用方会标记 abort，
            // 由 runner 重置并销毁专用非池化物理会话。
            LogWarningNoThrow(
                logger,
                "ETL MySQL 目标写者锁释放失败 exceptionType={ExceptionType}",
                releaseException.GetType().FullName ??
                releaseException.GetType().Name);
        }
        return releaseConfirmed;
    }
}

/// <summary>
/// Writer 与 runner 之间的目标会话清理协议。回滚或命名锁释放无法确认时，
/// runner 必须销毁非池化 MySQL 物理会话，不得把它返回共享池。
/// </summary>
internal sealed class EtlTargetConnectionCleanupState
{
    private int _requiresPhysicalSessionAbort;

    internal bool RequiresPhysicalSessionAbort =>
        Volatile.Read(ref _requiresPhysicalSessionAbort) != 0;

    internal void RequirePhysicalSessionAbort()
        => Interlocked.Exchange(ref _requiresPhysicalSessionAbort, 1);
}

/// <summary>
/// 目标连接的唯一释放者。MySQL abort 路径依赖工厂强制的
/// Pooling=false + CancellationTimeout=-1 + 有界 DefaultCommandTimeout：10s
/// Reset 只是一轮有界尝试；随后 Dispose 内部回滚达到 60s 命令超时才会硬断 socket。
/// </summary>
internal sealed class EtlTargetConnectionLease : IAsyncDisposable
{
    private readonly DbConnection _connection;
    private readonly EtlTargetConnectionCleanupState _cleanupState;
    private readonly int _cleanupTimeoutSeconds;
    private readonly ILogger _logger;
    private int _disposed;

    internal EtlTargetConnectionLease(
        DbConnection connection,
        EtlTargetConnectionCleanupState cleanupState,
        int cleanupTimeoutSeconds,
        ILogger logger)
    {
        _connection = connection;
        _cleanupState = cleanupState;
        _cleanupTimeoutSeconds = cleanupTimeoutSeconds;
        _logger = logger;
    }

    internal DbConnection Connection => _connection;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_cleanupState.RequiresPhysicalSessionAbort &&
            _connection is MySqlConnection mySqlConnection)
        {
            ValidateAbortProfileNoThrow(mySqlConnection);
            LogNoThrow(
                LogLevel.Warning,
                "ETL MySQL 目标会话状态不可确认；正在销毁非池化物理会话");
            await ResetMySqlSessionNoThrowAsync(mySqlConnection);
        }

        try
        {
            await _connection.DisposeAsync();
        }
        catch (Exception disposeException)
        {
            // 释放故障不得覆盖 COMMIT 未知态或已确认成功。MySQL abort
            // profile 会在命令超时时关闭本地 socket，不会把会话返回池。
            LogNoThrow(
                LogLevel.Warning,
                "ETL 目标物理会话销毁异常 abort={Abort} exceptionType={ExceptionType}",
                _cleanupState.RequiresPhysicalSessionAbort,
                disposeException.GetType().FullName ??
                disposeException.GetType().Name);
        }
    }

    private void ValidateAbortProfileNoThrow(MySqlConnection connection)
    {
        try
        {
            var options = new MySqlConnectionStringBuilder(
                connection.ConnectionString);
            if (options.Pooling ||
                options.CancellationTimeout != -1 ||
                options.DefaultCommandTimeout == 0)
            {
                LogNoThrow(
                    LogLevel.Critical,
                    "ETL MySQL abort profile 失效 pooling={Pooling} cancellationTimeout={CancellationTimeout} commandTimeout={CommandTimeout}",
                    options.Pooling,
                    options.CancellationTimeout,
                    options.DefaultCommandTimeout);
            }
        }
        catch (Exception profileException)
        {
            LogNoThrow(
                LogLevel.Critical,
                "ETL MySQL abort profile 无法验证 exceptionType={ExceptionType}",
                profileException.GetType().FullName ??
                profileException.GetType().Name);
        }
    }

    private async Task ResetMySqlSessionNoThrowAsync(
        MySqlConnection connection)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(
                TimeSpan.FromSeconds(_cleanupTimeoutSeconds));
            await connection.ResetConnectionAsync(cleanup.Token);
        }
        catch (Exception resetException)
        {
            // COM_RESET_CONNECTION 成功会回滚事务并释放 GET_LOCK；10s token 只约束
            // 本次 Reset 尝试，不保证硬断 socket。随后仍只做一次非池化连接 Dispose，
            // 其内部回滚由 60s DefaultCommandTimeout + CancellationTimeout=-1 硬中止兜底。
            LogNoThrow(
                LogLevel.Warning,
                "ETL MySQL 目标会话重置失败 exceptionType={ExceptionType}",
                resetException.GetType().FullName ??
                resetException.GetType().Name);
        }
    }

    private void LogNoThrow(
        LogLevel level,
        string message,
        params object?[] arguments)
    {
        try
        {
            _logger.Log(level, message, arguments);
        }
        catch
        {
            // 可观测性提供方不得阻断物理会话销毁。
        }
    }
}
