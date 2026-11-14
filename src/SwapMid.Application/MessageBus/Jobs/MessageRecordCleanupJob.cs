using System.Threading.Tasks;
using SwapMid.Scheduling;
using Volo.Abp.DependencyInjection;

namespace SwapMid.MessageBus;

/// <summary>
/// 消息投递记录清理 Hangfire 入口。统一经 <see cref="ScheduledJobRunner"/> 驱动
/// <see cref="MessageRecordCleanupHandler"/>：记执行历史 + 失败告警 + 并发闸保护。
/// </summary>
public class MessageRecordCleanupJob : IRunnerDrivenJob, ITransientDependency
{
    private readonly ScheduledJobRunner _runner;

    public MessageRecordCleanupJob(ScheduledJobRunner runner)
    {
        _runner = runner;
    }

    public Task ExecuteAsync(System.Threading.CancellationToken cancellationToken)
        => _runner.RunForHangfireAsync(
            ScheduledJobKeys.MessageRecordCleanup,
            new JobRunContext { TriggerKind = JobTriggerKind.Scheduled },
            cancellationToken);
}
