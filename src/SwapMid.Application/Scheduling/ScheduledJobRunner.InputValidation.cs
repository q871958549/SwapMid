using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SwapMid.Scheduling;

public partial class ScheduledJobRunner
{
    /// <summary>
    /// 在抢占任务并发闸前记录输入拒绝。无效请求不能抢闸阻塞合法任务，也不能在闸忙时被伪装成成功跳过。
    /// </summary>
    private async Task<RunnerOutcome> RecordRejectedInputAsync(
        string jobKey,
        IScheduledJobHandler handler,
        JobRunContext context,
        string summary)
    {
        var executionStart = await _recorder.StartAsync(
            jobKey,
            handler.GetType().Name,
            "RunAsync",
            null,
            context.TriggerKind,
            context.IsDryRun,
            context.ArgsJson);
        var terminalStateRecorded = await _recorder.FinishAsync(
            executionStart.ExecutionId,
            false,
            summary,
            null);

        var failureNotificationHandled = context.IsDryRun;
        if (!context.IsDryRun && terminalStateRecorded)
        {
            failureNotificationHandled =
                await TryNotifyFailureWithSuppressionAsync(jobKey, executionStart.ExecutionId, summary);
        }
        else if (!terminalStateRecorded)
        {
            _logger.LogWarning(
                "定时任务 {JobKey} 参数被拒绝，但失败终态未确认落库；Hangfire fallback 将补记",
                jobKey);
        }

        return new RunnerOutcome(
            JobRunResult.Fail(summary),
            executionStart.ExecutionId,
            terminalStateRecorded,
            failureNotificationHandled);
    }
}
