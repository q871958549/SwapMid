using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace SwapMid.Scheduling;

/// <summary>
/// 审计日志清理定时任务入口。统一经 <see cref="ScheduledJobRunner"/> 驱动 <see cref="AuditLogCleanupHandler"/>
/// （记执行历史 + 失败告警 + 演练支持）。实现 <see cref="IRunnerDrivenJob"/> → ScheduledJobExecutionFilter
/// 自动跳过，避免与 Runner 双重记录。原「直接删」逻辑已迁入 handler（含演练预览删除量）。
/// </summary>
public class AuditLogCleanupJob : IRunnerDrivenJob, ITransientDependency
{
    private readonly ScheduledJobRunner _runner;

    public AuditLogCleanupJob(ScheduledJobRunner runner)
    {
        _runner = runner;
    }

    public Task ExecuteAsync(System.Threading.CancellationToken cancellationToken)
        => _runner.RunForHangfireAsync(
            ScheduledJobKeys.AuditLogCleanup,
            new JobRunContext { TriggerKind = JobTriggerKind.Scheduled },
            cancellationToken);
}
