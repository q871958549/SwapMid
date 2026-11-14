using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace SwapMid.Scheduling;

/// <summary>
/// 执行历史清理定时任务入口。统一经 <see cref="ScheduledJobRunner"/> 驱动 handler（记历史 + 失败告警）。
/// 实现 <see cref="IRunnerDrivenJob"/> → <c>ScheduledJobExecutionFilter</c> 自动跳过，避免与 Runner 双重记录。
/// </summary>
public class ExecutionHistoryCleanupJob : IRunnerDrivenJob, ITransientDependency
{
    private readonly ScheduledJobRunner _runner;

    public ExecutionHistoryCleanupJob(ScheduledJobRunner runner)
    {
        _runner = runner;
    }

    public Task ExecuteAsync(System.Threading.CancellationToken cancellationToken)
        => _runner.RunForHangfireAsync(
            ScheduledJobKeys.ExecutionHistoryCleanup,
            new JobRunContext { TriggerKind = JobTriggerKind.Scheduled },
            cancellationToken);
}
