using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.Scheduling;

public partial class ScheduledJobRunner
{
    /// <summary>
    /// Hangfire 专用入口：复用 <see cref="RunAsync"/> 的完整记录、告警与分发语义；
    /// 当统一管道返回失败终态时再抛出低敏 typed exception，使 Hangfire 如实进入 Failed。
    /// 控制页同步执行必须继续调用 <see cref="RunAsync"/>，保持 HTTP 200 / Success=false 的兼容信封。
    /// </summary>
    public Task<JobRunResult> RunForHangfireAsync(
        string jobKey,
        JobRunContext context,
        CancellationToken cancellationToken = default)
    {
        // async method builder 会把任何携带已取消 token 的 OCE 派生类归一成 Canceled Task，
        // await 时只剩 TaskCanceledException，typed marker 元数据随之丢失。显式 TCS 以 Faulted
        // 保存 marker：Hangfire 仍按 OCE 识别 worker shutdown 并重新入队，Host filter 也能读取
        // executionId/持久化确认位，避免重复补历史。
        var completion = new TaskCompletionSource<JobRunResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ = RunCoreAsync(jobKey, context, cancellationToken).ContinueWith(
            completed =>
            {
                if (completed.IsCanceled)
                {
                    completion.TrySetException(new TaskCanceledException(completed));
                    return;
                }

                if (completed.IsFaulted)
                {
                    completion.TrySetException(completed.Exception.InnerExceptions);
                    return;
                }

                try
                {
                    completion.TrySetResult(ResolveHangfireOutcome(
                        completed.Result,
                        jobKey));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return completion.Task;
    }

    private static JobRunResult ResolveHangfireOutcome(
        RunnerOutcome outcome,
        string jobKey)
    {
        if (outcome.Cancellation != null)
        {
            throw new ScheduledJobRecordedCancellationException(
                jobKey,
                outcome.ExecutionId,
                outcome.TerminalStateRecorded,
                outcome.Cancellation.CancellationToken);
        }

        if (outcome.Result.Status == JobExecutionStatus.Failed)
        {
            throw new ScheduledJobRecordedFailureException(
                jobKey,
                outcome.ExecutionId,
                outcome.TerminalStateRecorded,
                outcome.FailureNotificationHandled);
        }

        if (!outcome.TerminalStateRecorded)
        {
            if (outcome.Result.Status == JobExecutionStatus.Skipped)
            {
                throw new ScheduledJobUnconfirmedSkippedException(
                    jobKey,
                    outcome.ExecutionId);
            }

            throw new ScheduledJobUnconfirmedSuccessException(
                jobKey,
                outcome.ExecutionId);
        }

        return outcome.Result;
    }
}
