using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// 共享 SQLite 宿主全局禁用数据库事务，只验证协调器状态与调用顺序；
/// 真实事务要求及原子回滚由 PostgreSQL 18 契约覆盖。
/// </summary>
internal sealed class TestPipelineTerminalUnitOfWorkGuard :
    IPipelineTerminalUnitOfWorkGuard
{
    public void EnsureTransactional()
    {
    }
}

internal sealed class TestPipelineTerminalStoreTopologyGuard :
    IPipelineTerminalStoreTopologyGuard
{
    private readonly ConcurrentQueue<PipelineTerminalStoreTopologyPhase>
        _calls = new();
    private readonly ConcurrentQueue<string> _safeScopeDefaults = new();
    private readonly AsyncLocal<ScopeHolder?> _scope = new();
    private string _configuredDefault = "sqlite:test-default";
    private int _reject;
    private int _rejectedPhase = -1;

    public IReadOnlyCollection<PipelineTerminalStoreTopologyPhase> Calls
        => _calls.ToArray();

    public IReadOnlyCollection<string> SafeScopeDefaults
        => _safeScopeDefaults.ToArray();

    public string ConfiguredDefault
    {
        get => Volatile.Read(ref _configuredDefault);
        set => Volatile.Write(ref _configuredDefault, value);
    }

    public bool IsScopeActive =>
        _scope.Value is { } holder &&
        !Volatile.Read(ref holder.Revoked);

    public string? CurrentPinnedDefault => IsScopeActive
        ? _scope.Value!.PinnedDefault
        : null;

    public Task<IPipelineTerminalStoreTopologyPin> CaptureSafeAsync(
        PipelineTerminalStoreTopologyPhase phase,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RecordOrReject(phase);
        return Task.FromResult<IPipelineTerminalStoreTopologyPin>(
            new TestPin(
                ConfiguredDefault,
                RequiresBackgroundJobs(phase)));
    }

    public IDisposable EnterSafeScope(
        IPipelineTerminalStoreTopologyPin pin,
        PipelineTerminalStoreTopologyPhase phase)
    {
        RecordOrReject(phase);
        if (pin is not TestPin testPin ||
            Interlocked.Exchange(ref testPin.Consumed, 1) != 0 ||
            _scope.Value != null ||
            (RequiresBackgroundJobs(phase) &&
             !testPin.AllowsBackgroundJobs))
        {
            throw CoordinationUnavailable();
        }

        var holder = new ScopeHolder(
            testPin.PinnedDefault,
            testPin.AllowsBackgroundJobs);
        _scope.Value = holder;
        return new TestScope(this, holder);
    }

    public void EnsureSafeScope(PipelineTerminalStoreTopologyPhase phase)
    {
        RecordOrReject(phase);
        var holder = _scope.Value;
        if (holder == null ||
            Volatile.Read(ref holder.Revoked) ||
            (RequiresBackgroundJobs(phase) &&
             !holder.AllowsBackgroundJobs))
        {
            throw CoordinationUnavailable();
        }

        _safeScopeDefaults.Enqueue(holder.PinnedDefault);
    }

    private void RecordOrReject(PipelineTerminalStoreTopologyPhase phase)
    {
        _calls.Enqueue(phase);
        if (Volatile.Read(ref _reject) == 1 ||
            Volatile.Read(ref _rejectedPhase) == (int)phase)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionCoordinationUnavailable);
        }
    }

    public void Reject() => Volatile.Write(ref _reject, 1);

    public void RejectAt(PipelineTerminalStoreTopologyPhase phase)
        => Volatile.Write(ref _rejectedPhase, (int)phase);

    public void Reset()
    {
        if (_scope.Value is { } holder)
        {
            Volatile.Write(ref holder.Revoked, true);
            _scope.Value = null;
        }

        Volatile.Write(ref _reject, 0);
        Volatile.Write(ref _rejectedPhase, -1);
        ConfiguredDefault = "sqlite:test-default";
        while (_calls.TryDequeue(out _))
        {
        }
        while (_safeScopeDefaults.TryDequeue(out _))
        {
        }
    }

    private void Exit(ScopeHolder holder)
    {
        Volatile.Write(ref holder.Revoked, true);
        if (ReferenceEquals(_scope.Value, holder))
        {
            _scope.Value = null;
        }
    }

    private static BusinessException CoordinationUnavailable()
        => new(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);

    private static bool RequiresBackgroundJobs(
        PipelineTerminalStoreTopologyPhase phase)
        => phase is not (
            PipelineTerminalStoreTopologyPhase.TargetReceiptVerification or
            PipelineTerminalStoreTopologyPhase.TargetReceiptActivation or
            PipelineTerminalStoreTopologyPhase.OutcomeResolutionApplied);

    private sealed class TestScope(
        TestPipelineTerminalStoreTopologyGuard owner,
        ScopeHolder holder) : IDisposable
    {
        private TestPipelineTerminalStoreTopologyGuard? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Exit(holder);
        }
    }

    private sealed class TestPin(
        string pinnedDefault,
        bool allowsBackgroundJobs) :
        IPipelineTerminalStoreTopologyPin
    {
        public string PinnedDefault { get; } = pinnedDefault;
        public bool AllowsBackgroundJobs { get; } = allowsBackgroundJobs;
        public int Consumed;
    }

    private sealed class ScopeHolder(
        string pinnedDefault,
        bool allowsBackgroundJobs)
    {
        public string PinnedDefault { get; } = pinnedDefault;
        public bool AllowsBackgroundJobs { get; } = allowsBackgroundJobs;
        public bool Revoked;
    }
}
