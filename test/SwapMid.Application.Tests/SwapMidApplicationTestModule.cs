using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Modularity;

namespace SwapMid;

[DependsOn(
    typeof(SwapMidApplicationModule),
    typeof(SwapMidDomainTestModule)
)]
public class SwapMidApplicationTestModule : AbpModule
{
    public override void PostConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Replace(
            ServiceDescriptor.Singleton<IAbpDistributedLock, TestAbpDistributedLock>());
    }
}

internal sealed class TestAbpDistributedLock : IAbpDistributedLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.Ordinal);

    public async Task<IAbpDistributedLockHandle?> TryAcquireAsync(
        string name,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(name, static _ => new SemaphoreSlim(1, 1));
        var acquired = timeout == Timeout.InfiniteTimeSpan
            ? await WaitWithoutTimeoutAsync(semaphore, cancellationToken)
            : await semaphore.WaitAsync(timeout, cancellationToken);
        return acquired ? new Handle(semaphore) : null;
    }

    private static async Task<bool> WaitWithoutTimeoutAsync(
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        return true;
    }

    private sealed class Handle(SemaphoreSlim semaphore) :
        IAbpDistributedLockHandle,
        DataIntegration.IDataPipelineLeaseLossAwareHandle
    {
        private readonly CancellationTokenSource _handleLost = new();
        private int _released;

        public CancellationToken HandleLostToken => _handleLost.Token;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
                _handleLost.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}
