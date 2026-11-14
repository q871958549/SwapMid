using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace SwapMid.Infrastructure;

/// <summary>
/// 外部数据源进程内并发闸。调用方必须使用固定低基数 key，并由启动期已校验的配置给出并发数。
/// 本闸不排队：容量耗尽立即返回 null，避免公开请求在进程内继续堆积。
/// </summary>
public interface IExternalDataSourceConcurrencyGate
{
    ValueTask<IAsyncDisposable?> TryAcquireAsync(
        string key,
        int maxConcurrency,
        CancellationToken cancellationToken);
}

public sealed class ExternalDataSourceConcurrencyGate :
    IExternalDataSourceConcurrencyGate,
    ISingletonDependency
{
    private readonly ConcurrentDictionary<string, GateState> _gates =
        new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(
        string key,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        cancellationToken.ThrowIfCancellationRequested();

        var state = _gates.GetOrAdd(
            key,
            static (_, concurrency) => new GateState(concurrency),
            maxConcurrency);
        if (state.MaxConcurrency != maxConcurrency)
        {
            throw new InvalidOperationException(
                $"外部数据源并发闸 {key} 的容量配置在进程内不一致：" +
                $"{state.MaxConcurrency} != {maxConcurrency}。");
        }

        return await state.Semaphore.WaitAsync(0, cancellationToken)
            ? new GateLease(state.Semaphore)
            : null;
    }

    private sealed class GateState(int maxConcurrency)
    {
        public int MaxConcurrency { get; } = maxConcurrency;
        public SemaphoreSlim Semaphore { get; } =
            new(maxConcurrency, maxConcurrency);
    }

    private sealed class GateLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
