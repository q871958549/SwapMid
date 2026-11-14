using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.MultiTenancy;

namespace SwapMid.DataIntegration;

/// <summary>
/// SQLite 集成测试使用的进程内执行栅栏。与生产实现一致，同时占用管道和全部目标资源，
/// 并暴露可控的持有权丢失信号；它只验证协调器语义，不模拟 PostgreSQL advisory lock。
/// </summary>
internal sealed class TestDataPipelineExecutionFence
    : IDataPipelineExecutionFence
{
    private readonly ICurrentTenant _currentTenant;
    private readonly ConcurrentDictionary<string, byte> _held =
        new(StringComparer.Ordinal);
    private readonly object _observationLock = new();
    private Handle? _lastHandle;
    private Guid? _lastObservedTenantId;
    private IReadOnlyList<string> _lastTargetResources = Array.Empty<string>();
    private int _acquisitionCalls;
    private int _rejectNextAcquisition;

    public int AcquisitionCalls => Volatile.Read(ref _acquisitionCalls);

    public Guid? LastObservedTenantId
    {
        get
        {
            lock (_observationLock)
            {
                return _lastObservedTenantId;
            }
        }
    }

    public IReadOnlyList<string> LastTargetResources
    {
        get
        {
            lock (_observationLock)
            {
                return _lastTargetResources.ToArray();
            }
        }
    }

    public Exception? ExceptionToThrow { get; set; }
    public Action? AcquisitionCallback { get; set; }

    public TestDataPipelineExecutionFence(ICurrentTenant currentTenant)
    {
        _currentTenant = currentTenant;
    }

    public void Reset()
    {
        ExceptionToThrow = null;
        AcquisitionCallback = null;
        Interlocked.Exchange(ref _acquisitionCalls, 0);
        Interlocked.Exchange(ref _rejectNextAcquisition, 0);
        Interlocked.Exchange(ref _lastHandle, null);
        lock (_observationLock)
        {
            _lastObservedTenantId = null;
            _lastTargetResources = Array.Empty<string>();
        }
        _held.Clear();
    }

    public void RejectNextAcquisition()
        => Interlocked.Exchange(ref _rejectNextAcquisition, 1);

    public void LoseCurrentLease()
        => Volatile.Read(ref _lastHandle)?.LoseLease();

    public Task<DataPipelineExecutionFenceAcquireResult> TryAcquireAsync(
        Guid pipelineId,
        IReadOnlyCollection<string> targetResources,
        int capacitySlots,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        lock (_observationLock)
        {
            _lastObservedTenantId = _currentTenant.Id;
            _lastTargetResources = targetResources
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }
        Interlocked.Increment(ref _acquisitionCalls);
        AcquisitionCallback?.Invoke();
        if (ExceptionToThrow is { } exception)
        {
            return Task.FromException<DataPipelineExecutionFenceAcquireResult>(
                exception);
        }

        if (Interlocked.Exchange(ref _rejectNextAcquisition, 0) == 1)
        {
            return Task.FromResult(
                DataPipelineExecutionFenceAcquireResult.Rejected(
                    DataPipelineExecutionFenceRejection.ResourceBusy));
        }

        var keys = new List<string>();
        if (capacitySlots > 0)
        {
            var capacityKey = Enumerable.Range(0, capacitySlots)
                .Select(slot => $"capacity:{slot}")
                .FirstOrDefault(key => _held.TryAdd(key, 0));
            if (capacityKey == null)
            {
                return Task.FromResult(
                    DataPipelineExecutionFenceAcquireResult.Rejected(
                        DataPipelineExecutionFenceRejection.CapacityExhausted));
            }

            keys.Add(capacityKey);
        }

        keys.AddRange(targetResources
            .Select(targetResource => $"target:{targetResource}")
            .Append($"pipeline:{pipelineId:N}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal));
        var acquired = new List<string>(keys.Count);
        if (capacitySlots > 0)
        {
            acquired.Add(keys[0]);
        }

        foreach (var key in keys.Skip(capacitySlots > 0 ? 1 : 0))
        {
            if (_held.TryAdd(key, 0))
            {
                acquired.Add(key);
                continue;
            }

            Release(acquired);
            return Task.FromResult(
                DataPipelineExecutionFenceAcquireResult.Rejected(
                    DataPipelineExecutionFenceRejection.ResourceBusy));
        }

        var handle = new Handle(this, acquired.ToArray());
        Volatile.Write(ref _lastHandle, handle);
        return Task.FromResult(
            DataPipelineExecutionFenceAcquireResult.Acquired(handle));
    }

    private void Release(IEnumerable<string> keys)
    {
        foreach (var key in keys.Reverse())
        {
            _held.TryRemove(key, out _);
        }
    }

    private sealed class Handle(
        TestDataPipelineExecutionFence owner,
        IReadOnlyList<string> keys)
        : IDataPipelineExecutionFenceHandle
    {
        private readonly CancellationTokenSource _lost = new();
        private int _disposed;

        public CancellationToken HandleLostToken => _lost.Token;

        public void LoseLease()
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _lost.Cancel();
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(keys);
                _lost.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}
