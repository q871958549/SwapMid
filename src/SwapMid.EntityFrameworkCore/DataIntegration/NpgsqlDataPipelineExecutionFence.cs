using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// 独立非池化 Npgsql 连接持有的 session advisory lock。它同时锁定管道与全部目标资源，既阻止
/// 同一管道双跑，也阻止两个管道并发修改任一相同目标；心跳失败会取消提交点之前的 ETL。
/// </summary>
public sealed class NpgsqlDataPipelineExecutionFence
    : IDataPipelineExecutionFence, ITransientDependency
{
    private readonly IDataPipelineExecutionFenceConnectionFactory _connectionFactory;
    private readonly ILogger<NpgsqlDataPipelineExecutionFence> _logger;

    public NpgsqlDataPipelineExecutionFence(
        IDataPipelineExecutionFenceConnectionFactory connectionFactory,
        ILogger<NpgsqlDataPipelineExecutionFence> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<DataPipelineExecutionFenceAcquireResult> TryAcquireAsync(
        Guid pipelineId,
        IReadOnlyCollection<string> targetResources,
        int capacitySlots,
        CancellationToken cancellationToken = default)
    {
        if (capacitySlots is < 0 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(capacitySlots));
        }
        ArgumentNullException.ThrowIfNull(targetResources);
        if (targetResources.Count is < 1 or
            > DataPipelineExecutionFenceLimits.MaxTargetResources)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetResources),
                targetResources.Count,
                $"Target resource count must be between 1 and {DataPipelineExecutionFenceLimits.MaxTargetResources}.");
        }
        if (targetResources.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Target resources cannot contain null or whitespace values.",
                nameof(targetResources));
        }
        if (Transaction.Current != null)
        {
            throw new InvalidOperationException(
                "ETL execution fence cannot enlist in an ambient transaction.");
        }

        var connection = await _connectionFactory.CreateAsync();
        // pipeline 与去重后的全部目标键统一按最终 advisory key 排序，确保不同调用方即使
        // 以相反顺序传入 legacy/verified 身份，也始终按同一顺序获取并在失败时整体释放。
        var resourceKeys = targetResources
            .Select(targetResource =>
                HashLockKey($"swapmid:data-integration:target:{targetResource}"))
            .Append(HashLockKey(
                $"swapmid:data-integration:pipeline:{pipelineId:N}"))
            .Distinct()
            .OrderBy(key => key)
            .ToArray();
        var acquired = new List<long>(resourceKeys.Length + 1);

        try
        {
            await connection.OpenAsync(cancellationToken);
            if (capacitySlots > 0)
            {
                var startSlot = (int)(unchecked((ulong)HashLockKey(
                    $"swapmid:data-integration:capacity-start:{pipelineId:N}")) %
                    (ulong)capacitySlots);
                var capacityAcquired = false;
                for (var offset = 0; offset < capacitySlots; offset++)
                {
                    var slot = (startSlot + offset) % capacitySlots;
                    var key = HashLockKey(
                        $"swapmid:data-integration:capacity:{slot}");
                    if (!await TryAcquireKeyAsync(
                            connection,
                            key,
                            cancellationToken))
                    {
                        continue;
                    }

                    acquired.Add(key);
                    capacityAcquired = true;
                    break;
                }

                if (!capacityAcquired)
                {
                    await connection.DisposeAsync();
                    return DataPipelineExecutionFenceAcquireResult.Rejected(
                        DataPipelineExecutionFenceRejection.CapacityExhausted);
                }
            }

            foreach (var key in resourceKeys)
            {
                if (acquired.Contains(key))
                {
                    continue;
                }

                if (!await TryAcquireKeyAsync(
                        connection,
                        key,
                        cancellationToken))
                {
                    await ReleaseKeysNoThrowAsync(connection, acquired);
                    await connection.DisposeAsync();
                    return DataPipelineExecutionFenceAcquireResult.Rejected(
                        DataPipelineExecutionFenceRejection.ResourceBusy);
                }

                acquired.Add(key);
            }

            return DataPipelineExecutionFenceAcquireResult.Acquired(
                new Handle(connection, acquired.ToArray(), _logger));
        }
        catch
        {
            await ReleaseKeysNoThrowAsync(connection, acquired);
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<bool> TryAcquireKeyAsync(
        NpgsqlConnection connection,
        long key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@lockKey)";
        command.Parameters.AddWithValue("lockKey", key);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task ReleaseKeysNoThrowAsync(
        NpgsqlConnection connection,
        IEnumerable<long> keys)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        foreach (var key in keys.Reverse())
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@lockKey)";
                command.Parameters.AddWithValue("lockKey", key);
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
            catch
            {
                // 连接关闭会由 PostgreSQL 释放该 session 的全部 advisory lock。
                return;
            }
        }
    }

    private static long HashLockKey(string resource)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resource));
        return BinaryPrimitives.ReadInt64LittleEndian(hash);
    }

    internal static void SignalHandleLostNoThrow(
        CancellationTokenSource lost,
        ILogger logger,
        Exception heartbeatException)
    {
        // 执行权失效是安全状态转换，必须先于且独立于可观测性提供方。
        // Cancel 回调即使抛异常，CancellationToken 也已进入取消态；这里不得让异常
        // 阻止后续清理或让 heartbeat Task 以日志故障代替失联信号。
        try
        {
            lost.Cancel();
        }
        catch
        {
            // Dispose 竞态或第三方取消回调异常均不能反转已发出的失联状态。
        }

        try
        {
            logger.LogError(
                "ETL PostgreSQL session fence 心跳失败 exceptionType={ExceptionType}",
                heartbeatException.GetType().FullName ??
                heartbeatException.GetType().Name);
        }
        catch
        {
            // 可观测性提供方不得吞掉 fence 失联信号。
        }
    }

    private sealed class Handle : IDataPipelineExecutionFenceHandle
    {
        private readonly NpgsqlConnection _connection;
        private readonly long[] _keys;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _stop = new();
        private readonly CancellationTokenSource _lost = new();
        private readonly SemaphoreSlim _commandGate = new(1, 1);
        private readonly Task _heartbeat;
        private int _disposed;

        public Handle(
            NpgsqlConnection connection,
            long[] keys,
            ILogger logger)
        {
            _connection = connection;
            _keys = keys;
            _logger = logger;
            _heartbeat = RunHeartbeatAsync();
        }

        public CancellationToken HandleLostToken => _lost.Token;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var releaseFailed = false;
            _stop.Cancel();
            try
            {
                await _heartbeat;
            }
            catch
            {
                releaseFailed = true;
            }

            try
            {
                await _commandGate.WaitAsync(CancellationToken.None);
                try
                {
                    using var cleanup = new CancellationTokenSource(
                        TimeSpan.FromSeconds(10));
                    foreach (var key in _keys.Reverse())
                    {
                        await using var command = _connection.CreateCommand();
                        command.CommandText =
                            "SELECT pg_advisory_unlock(@lockKey)";
                        command.Parameters.AddWithValue("lockKey", key);
                        if (await command.ExecuteScalarAsync(cleanup.Token)
                            is not true)
                        {
                            releaseFailed = true;
                        }
                    }
                }
                finally
                {
                    _commandGate.Release();
                }
            }
            catch
            {
                releaseFailed = true;
            }
            finally
            {
                try
                {
                    await _connection.DisposeAsync();
                }
                catch
                {
                    releaseFailed = true;
                }
                finally
                {
                    _commandGate.Dispose();
                    _stop.Dispose();
                    _lost.Dispose();
                }
            }

            if (releaseFailed)
            {
                throw new InvalidOperationException(
                    "ETL PostgreSQL session fence release failed.");
            }
        }

        private async Task RunHeartbeatAsync()
        {
            using var timer = new PeriodicTimer(
                TimeSpan.FromSeconds(
                    DataPipelineExecutionFenceSessionSettings.HeartbeatSeconds));
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token))
                {
                    await _commandGate.WaitAsync(_stop.Token);
                    try
                    {
                        await using var command = _connection.CreateCommand();
                        command.CommandText = "SELECT 1";
                        await command.ExecuteScalarAsync(_stop.Token);
                    }
                    finally
                    {
                        _commandGate.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                // 正常释放。
            }
            catch (Exception heartbeatException)
            {
                SignalHandleLostNoThrow(
                    _lost,
                    _logger,
                    heartbeatException);
            }
        }
    }
}
