using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace SwapMid.Scheduling;

/// <summary>
/// 执行历史清理处理器（第 2 个 <see cref="IScheduledJobHandler"/> 接入方，也是「用平台清理平台自身」的自洽用例）：
/// 删除超过保留期的 <see cref="ScheduledJobExecution"/>。保留天数取配置 ScheduledJobExecutionCleanup:RetainDays（缺省 90）。
/// 演练=只统计将删条数、不删；正式=删除并回报条数。不产出附件、不发邮件，仅记执行历史与摘要。
/// </summary>
// 必须显式暴露 IScheduledJobHandler：ABP 默认约定不暴露非命名匹配接口，漏标则 Runner 的
// IEnumerable&lt;IScheduledJobHandler&gt; 收集不到本 handler、运行期抛「未找到 handler」（同 ExcessMaterialReportHandler 教训）。
[ExposeServices(typeof(IScheduledJobHandler), typeof(ExecutionHistoryCleanupHandler))]
public class ExecutionHistoryCleanupHandler : IScheduledJobHandler, ITransientDependency
{
    // 与 catalog / job entry 共用 Domain.Shared 的常量源，避免跨层字面量重复。
    public string JobKey => ScheduledJobKeys.ExecutionHistoryCleanup;

    private readonly IRepository<ScheduledJobExecution, Guid> _repository;
    private readonly IConfiguration _configuration;
    private readonly IClock _clock;

    public ExecutionHistoryCleanupHandler(
        IRepository<ScheduledJobExecution, Guid> repository,
        IConfiguration configuration,
        IClock clock)
    {
        _repository = repository;
        _configuration = configuration;
        _clock = clock;
    }

    public async Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken = default)
    {
        var retainDays = _configuration.GetValue("ScheduledJobExecutionCleanup:RetainDays", 90);
        if (retainDays < 1)
        {
            return JobRunResult.Fail($"保留天数 {retainDays} 不合法（须 ≥ 1）");
        }

        // StartedAt 由 recorder 用 IClock.Now 记（本地时间，平台未配 AbpClockOptions）；cutoff 同基准，避免用 UtcNow 造成时区偏差多删/少删。
        var cutoff = _clock.Now.AddDays(-retainDays);

        // 🔴 **只删终态，Running 一律不碰**（与 MessageRecordCleanupHandler 同一惯例）。
        // Running 表示「这条执行还没收尾」——删掉它，那次执行就从台账上凭空消失，而任务本身
        // 可能仍在跑。正常情况下 Host 启动时的僵尸清理会把超时的 Running 标成 Interrupted
        // （届时它自然进入本清理的范围），所以窗口很窄；但「窄」不等于「没有」，而删错的代价
        // 是审计记录不可恢复。
        //
        // 谓词**抽成一个表达式复用**：统计与删除必须严格同口径，原实现两处各写一遍
        // `x.StartedAt < cutoff`，改一处漏一处就会「报告说删 N 条、实际删了别的量」。
        Expression<Func<ScheduledJobExecution, bool>> expired = x =>
            x.StartedAt < cutoff
            && (x.Status == JobExecutionStatus.Succeeded
                || x.Status == JobExecutionStatus.Failed
                || x.Status == JobExecutionStatus.Interrupted
                || x.Status == JobExecutionStatus.Skipped);

        var count = await _repository.CountAsync(expired, cancellationToken);

        if (context.IsDryRun)
        {
            return JobRunResult.Ok($"演练：将清理 {count} 条早于 {cutoff:yyyy-MM-dd} 的终态执行历史（执行中的不删，未删除）");
        }

        if (count > 0)
        {
            await _repository.DeleteDirectAsync(expired, cancellationToken);
        }

        return JobRunResult.Ok($"已清理 {count} 条早于 {cutoff:yyyy-MM-dd} 的终态执行历史（执行中的不删）");
    }
}
