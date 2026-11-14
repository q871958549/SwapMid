using System;
using System.Threading;

namespace SwapMid.Scheduling;

/// <summary>
/// 标记由 <c>ScheduledJobRunner</c> 驱动的 ABP 后台作业参数。
/// Host 据此只识别对应的 Hangfire adapter，不能把所有 ABP adapter 一刀切为零重试。
/// </summary>
public interface IRunnerDrivenJobArgs
{
    /// <summary>
    /// 管控页与执行历史使用的稳定任务 key。ABP 手工后台入口没有
    /// <c>RecurringJobId</c>，Host 必须从参数中恢复该值，确保进入 Runner 前的失败仍可定位。
    /// </summary>
    string JobKey { get; }
}

/// <summary>
/// 标记必须由 Host 对其精确 ABP Hangfire 泛型 adapter 关闭自动重试的后台作业参数。
/// 该 marker 不表示作业由 <c>ScheduledJobRunner</c> 驱动，因此不得影响统一执行历史过滤器的分类。
/// </summary>
public interface INonRetryableAbpBackgroundJobArgs
{
}

/// <summary>
/// Runner 向 Host 传播的低敏终态 marker。只携带稳定任务 key、执行 id 与持久化确认位；
/// 不保留原异常、业务消息、参数或堆栈。取消 marker 另保留 Hangfire 注入的取消令牌，
/// 以维持框架的优雅停机重排语义。
/// </summary>
public interface IScheduledJobTerminalStateMarker
{
    string JobKey { get; }

    Guid ExecutionId { get; }

    bool TerminalStateRecorded { get; }
}

public abstract class ScheduledJobTerminalStateException
    : Exception, IScheduledJobTerminalStateMarker
{
    protected ScheduledJobTerminalStateException(
        string message,
        string jobKey,
        Guid executionId,
        bool terminalStateRecorded)
        : base(message)
    {
        JobKey = jobKey;
        ExecutionId = executionId;
        TerminalStateRecorded = terminalStateRecorded;
    }

    public string JobKey { get; }

    public Guid ExecutionId { get; }

    public bool TerminalStateRecorded { get; }
}

/// <summary>
/// Runner 已处理失败终态与业务告警后，向 Hangfire 传播的低敏失败 marker。
/// </summary>
public sealed class ScheduledJobRecordedFailureException : ScheduledJobTerminalStateException
{
    public ScheduledJobRecordedFailureException(
        string jobKey,
        Guid executionId,
        bool terminalStateRecorded,
        bool failureNotificationHandled)
        : base(
            terminalStateRecorded
                ? $"定时任务 {jobKey} 执行失败，终态已由统一执行管道记录"
                : $"定时任务 {jobKey} 执行失败，统一执行管道未能确认终态落库",
            jobKey,
            executionId,
            terminalStateRecorded)
    {
        FailureNotificationHandled = failureNotificationHandled;
    }

    /// <summary>
    /// 业务失败告警已发送，或被明确的演练/连续失败/未配置渠道策略处理。
    /// 真实发送失败时为 false，允许 Hangfire 通用告警再尝试一次。
    /// </summary>
    public bool FailureNotificationHandled { get; }
}

/// <summary>
/// Runner 已将取消历史收成 Interrupted 后向 Hangfire 传播的低敏取消 marker。
/// 必须保持 <see cref="OperationCanceledException"/> 类型：Hangfire 1.8 在 worker
/// shutdown token 取消时依靠该类型中止状态选举并重新入队；独立调用方取消才允许 Host 选为 Deleted。
/// </summary>
public sealed class ScheduledJobRecordedCancellationException
    : OperationCanceledException, IScheduledJobTerminalStateMarker
{
    public ScheduledJobRecordedCancellationException(
        string jobKey,
        Guid executionId,
        bool terminalStateRecorded,
        CancellationToken cancellationToken)
        : base($"定时任务 {jobKey} 已取消", innerException: null, cancellationToken)
    {
        JobKey = jobKey;
        ExecutionId = executionId;
        TerminalStateRecorded = terminalStateRecorded;
    }

    public string JobKey { get; }

    public Guid ExecutionId { get; }

    public bool TerminalStateRecorded { get; }
}

/// <summary>
/// 业务已成功但执行历史终态未确认时向 Hangfire 传播的低敏 marker。
/// 零自动重试保证外部副作用不会因记录异常而重放；Host 负责按原 id 优先补偿。
/// </summary>
public sealed class ScheduledJobUnconfirmedSuccessException : ScheduledJobTerminalStateException
{
    public ScheduledJobUnconfirmedSuccessException(string jobKey, Guid executionId)
        : base(
            $"定时任务 {jobKey} 已完成，但统一执行管道未能确认成功终态落库",
            jobKey,
            executionId,
            terminalStateRecorded: false)
    {
    }
}

/// <summary>
/// 业务因预期竞争跳过、但执行历史 Skipped 终态未确认时向 Hangfire 传播的低敏 marker。
/// 零自动重试避免把“没有执行权”升级成排队重放；Host 负责按原 id 优先补偿。
/// </summary>
public sealed class ScheduledJobUnconfirmedSkippedException : ScheduledJobTerminalStateException
{
    public ScheduledJobUnconfirmedSkippedException(string jobKey, Guid executionId)
        : base(
            $"定时任务 {jobKey} 已因竞争跳过，但统一执行管道未能确认 Skipped 终态落库",
            jobKey,
            executionId,
            terminalStateRecorded: false)
    {
    }
}
