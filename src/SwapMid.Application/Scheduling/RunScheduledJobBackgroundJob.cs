using System.Threading.Tasks;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace SwapMid.Scheduling;

/// <summary>「立即执行」后台化（F6）的入队参数：任务 key + 补偿参数。仅正式模式（演练必须同步等结果）。</summary>
public class RunScheduledJobArgs : IRunnerDrivenJobArgs
{
    public string JobKey { get; set; } = null!;

    public string? ArgsJson { get; set; }
}

/// <summary>
/// 旧版 ABP 后台承载，仅用于兼容升级前已经入队的作业。新任务由 Host 的
/// <c>RunScheduledJobHangfireJob</c> 直接入 Hangfire：ABP adapter 自身含 async/await，会把
/// typed OCE marker 归一成 Canceled Task，丢失 executionId/落库确认位。发布切换前必须排空
/// 本旧入口的 queued/processing 作业；保留类型是为了让存量 payload 仍可反序列化，不得恢复新入队。
/// </summary>
public class RunScheduledJobBackgroundJob : AsyncBackgroundJob<RunScheduledJobArgs>, ITransientDependency
{
    private readonly ScheduledJobRunner _runner;
    private readonly ICancellationTokenProvider _cancellationTokenProvider;

    public RunScheduledJobBackgroundJob(
        ScheduledJobRunner runner,
        ICancellationTokenProvider cancellationTokenProvider)
    {
        _runner = runner;
        _cancellationTokenProvider = cancellationTokenProvider;
    }

    [UnitOfWork(IsDisabled = true)]
    public override async Task ExecuteAsync(RunScheduledJobArgs args)
    {
        await _runner.RunForHangfireAsync(args.JobKey, new JobRunContext
        {
            TriggerKind = JobTriggerKind.Manual,
            IsDryRun = false,
            ArgsJson = args.ArgsJson is
                { Length: > JobRunContext.MaxArgsJsonCharacters }
                ? args.ArgsJson
                : string.IsNullOrWhiteSpace(args.ArgsJson)
                    ? null
                    : args.ArgsJson,
        }, _cancellationTokenProvider.Token);
    }
}
