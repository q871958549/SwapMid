using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace SwapMid.Scheduling;

/// <summary>
/// 通知发送日志清理 Hangfire 入口。统一经 <see cref="ScheduledJobRunner"/> 驱动
/// <see cref="NotificationLogCleanupHandler"/>：记执行历史 + 失败告警 + 并发闸保护。
/// </summary>
public class NotificationLogCleanupJob : IRunnerDrivenJob, ITransientDependency
{
    private readonly ScheduledJobRunner _runner;

    public NotificationLogCleanupJob(ScheduledJobRunner runner)
    {
        _runner = runner;
    }

    public Task ExecuteAsync(System.Threading.CancellationToken cancellationToken)
        => _runner.RunForHangfireAsync(
            ScheduledJobKeys.NotificationLogCleanup,
            new JobRunContext { TriggerKind = JobTriggerKind.Scheduled },
            cancellationToken);
}
