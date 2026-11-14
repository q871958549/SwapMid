using System;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.DataIntegration;

/// <summary>
/// receipt-v1 协调器的纯内存目标端替身。它不打开任何外部连接，并可分别模拟匹配、
/// 缺失、不匹配与暂不可达，以证明平台未知态只能由完整回执收敛。
/// </summary>
internal sealed class FakeEtlTargetReceiptStore : IEtlTargetReceiptStore
{
    internal const long DefaultProcessedRows = 37;
    internal const string DefaultSchemaHash =
        "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
    internal const string DefaultBatchDigest =
        "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";

    private int _probeCalls;
    private int _findCalls;
    private int _readFenceAcquireCalls;
    private int _readFenceDisposeCalls;
    private int _activeReadFences;
    private Func<bool>? _probeAmbientUnitOfWorkAccessor;
    private Func<Task>? _readFenceDisposeObserver;

    public FakeReceiptBehavior Behavior { get; set; } =
        FakeReceiptBehavior.Match;

    public int ProbeCalls => Volatile.Read(ref _probeCalls);

    public int FindCalls => Volatile.Read(ref _findCalls);

    public int ReadFenceAcquireCalls =>
        Volatile.Read(ref _readFenceAcquireCalls);

    public int ReadFenceDisposeCalls =>
        Volatile.Read(ref _readFenceDisposeCalls);

    public int ActiveReadFences => Volatile.Read(ref _activeReadFences);

    public bool? LastProbeHadAmbientUnitOfWork { get; private set; }

    public Action? ProbeCallback { get; set; }

    public EtlExecutionContext? LastExecutionContext { get; private set; }

    public void Reset()
    {
        Behavior = FakeReceiptBehavior.Match;
        LastExecutionContext = null;
        LastProbeHadAmbientUnitOfWork = null;
        ProbeCallback = null;
        _probeAmbientUnitOfWorkAccessor = null;
        _readFenceDisposeObserver = null;
        Interlocked.Exchange(ref _probeCalls, 0);
        Interlocked.Exchange(ref _findCalls, 0);
        Interlocked.Exchange(ref _readFenceAcquireCalls, 0);
        Interlocked.Exchange(ref _readFenceDisposeCalls, 0);
        Interlocked.Exchange(ref _activeReadFences, 0);
    }

    public void ObserveProbeAmbientUnitOfWork(Func<bool> accessor)
    {
        _probeAmbientUnitOfWorkAccessor = accessor;
    }

    public void ObserveReadFenceDispose(Func<Task> observer)
    {
        _readFenceDisposeObserver = observer;
    }

    public Task<EtlTargetReceiptActivation> ProbeActivationAsync(
        DataSource target,
        EtlTransformConfig transform,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastProbeHadAmbientUnitOfWork =
            _probeAmbientUnitOfWorkAccessor?.Invoke();
        Interlocked.Increment(ref _probeCalls);
        ProbeCallback?.Invoke();
        return Task.FromResult(new EtlTargetReceiptActivation(
            target.Id,
            "sqlserver",
            transform.TargetTable,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{target.Id:N}:{transform.TargetTable}"))),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{target.Id:N}:coordination")))));
    }

    public Task<EtlTargetReceiptEvidence?> FindAsync(
        DataSource target,
        EtlExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _findCalls);
        LastExecutionContext = executionContext;

        if (Behavior == FakeReceiptBehavior.Unavailable)
        {
            throw new TimeoutException("fake target receipt unavailable");
        }

        if (Behavior == FakeReceiptBehavior.Absent)
        {
            return Task.FromResult<EtlTargetReceiptEvidence?>(null);
        }

        var evidence = CreateMatchingEvidence(executionContext);
        if (Behavior == FakeReceiptBehavior.Mismatch)
        {
            evidence = evidence with
            {
                ExecutionContractHash =
                    new string('E', PipelineExecutionConsts.Sha256HexLength),
            };
        }

        return Task.FromResult<EtlTargetReceiptEvidence?>(evidence);
    }

    public Task<IEtlTargetReceiptReadFence> AcquireReadFenceAsync(
        DataSource target,
        EtlExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _readFenceAcquireCalls);
        LastExecutionContext = executionContext;

        if (Behavior == FakeReceiptBehavior.Unavailable)
        {
            throw new TimeoutException(
                "fake target receipt read fence unavailable");
        }

        EtlTargetReceiptEvidence? receipt = null;
        if (Behavior != FakeReceiptBehavior.Absent)
        {
            receipt = CreateMatchingEvidence(executionContext);
            if (Behavior == FakeReceiptBehavior.Mismatch)
            {
                receipt = receipt with
                {
                    ExecutionContractHash = new string(
                        'E',
                        PipelineExecutionConsts.Sha256HexLength),
                };
            }
        }

        Interlocked.Increment(ref _activeReadFences);
        return Task.FromResult<IEtlTargetReceiptReadFence>(
            new FakeReadFence(receipt, DisposeReadFenceAsync));
    }

    public Task<EtlTargetReceiptInsertResult> InsertOrReadAsync(
        DbConnection connection,
        DbTransaction transaction,
        EtlTargetReceiptProviderContext providerContext,
        EtlTargetReceiptEvidence evidence,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
        => throw new NotSupportedException(
            "SQLite 协调器测试使用 FakeEtlPipelineRunner，不进入目标写事务。");

    internal static EtlTargetReceiptEvidence CreateMatchingEvidence(
        EtlExecutionContext context)
        => new(
            context.RunId,
            context.PipelineId,
            context.TargetReceipt!.ResourceInstanceId,
            context.LegacyTargetResourceHash,
            context.VerifiedTargetResourceHash!,
            context.ExecutionContractHash!,
            context.SourceQueryHash,
            DefaultSchemaHash,
            DefaultBatchDigest,
            context.WriteMode,
            DefaultProcessedRows);

    private async ValueTask DisposeReadFenceAsync()
    {
        try
        {
            var observer = _readFenceDisposeObserver;
            if (observer != null)
            {
                await observer();
            }
        }
        finally
        {
            Interlocked.Increment(ref _readFenceDisposeCalls);
            Interlocked.Decrement(ref _activeReadFences);
        }
    }

    private sealed class FakeReadFence : IEtlTargetReceiptReadFence
    {
        private readonly Func<ValueTask> _dispose;
        private int _disposed;

        internal FakeReadFence(
            EtlTargetReceiptEvidence? receipt,
            Func<ValueTask> dispose)
        {
            Receipt = receipt;
            _dispose = dispose;
        }

        public EtlTargetReceiptEvidence? Receipt { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            await _dispose();
        }
    }
}

internal enum FakeReceiptBehavior
{
    Match,
    Absent,
    Mismatch,
    Unavailable,
}
