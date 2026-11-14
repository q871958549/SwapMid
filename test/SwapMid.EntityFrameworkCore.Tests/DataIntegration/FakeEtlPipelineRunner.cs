using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

/// <summary>
/// <see cref="IEtlPipelineRunner"/> 的测试替身:不连真源/目标库,直接按预设返回 ETL 结果。
/// 注册为单例(见 SwapMidEntityFrameworkCoreTestModule),让管道激活→执行→暂停的
/// 状态流转与执行落库可在不连库下验证。默认返回成功(0 行);需要时测试可设 <see cref="Result"/> 改变返回行数。
/// </summary>
public class FakeEtlPipelineRunner : IEtlPipelineRunner
{
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private TaskCompletionSource<bool> _entered = NewSignal();
    private TaskCompletionSource<bool> _release = NewSignal();
    private int _runCalls;

    public FakeEtlPipelineRunner(IUnitOfWorkManager unitOfWorkManager)
    {
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary>RunAsync 的返回值,默认成功 0 行。</summary>
    public EtlRunResult Result { get; set; } = new(0, TargetTruncated: false);

    /// <summary>非空时让 RunAsync 返回故障任务,用于验证执行失败边界。</summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>是否让外部 ETL 停在已进入、尚未返回的边界。</summary>
    public bool ShouldBlock { get; private set; }

    public int RunCalls => Volatile.Read(ref _runCalls);

    public CancellationToken LastCancellationToken { get; private set; }

    public EtlExecutionContext? LastExecutionContext { get; private set; }

    public bool? ObservedAmbientUnitOfWork { get; private set; }

    public Task RunEntered => Volatile.Read(ref _entered).Task;

    public void Reset()
    {
        Volatile.Read(ref _release).TrySetResult(true);
        Result = new EtlRunResult(0, TargetTruncated: false);
        ExceptionToThrow = null;
        ShouldBlock = false;
        LastCancellationToken = default;
        LastExecutionContext = null;
        ObservedAmbientUnitOfWork = null;
        Interlocked.Exchange(ref _runCalls, 0);
        Volatile.Write(ref _entered, NewSignal());
        Volatile.Write(ref _release, NewSignal());
    }

    public void Block()
    {
        ShouldBlock = true;
        Volatile.Write(ref _release, NewSignal());
    }

    public void Release()
        => Volatile.Read(ref _release).TrySetResult(true);

    public async Task<EtlRunResult> RunAsync(
        EtlExecutionContext executionContext,
        DataSource source,
        DataSource target,
        string? transformConfig,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _runCalls);
        LastExecutionContext = executionContext;
        LastCancellationToken = ct;
        ObservedAmbientUnitOfWork = _unitOfWorkManager.Current != null;
        var release = Volatile.Read(ref _release);
        Volatile.Read(ref _entered).TrySetResult(true);

        if (ShouldBlock)
        {
            await release.Task.WaitAsync(ct);
        }

        if (ExceptionToThrow is { } exception)
        {
            throw exception;
        }

        return Result;
    }

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
