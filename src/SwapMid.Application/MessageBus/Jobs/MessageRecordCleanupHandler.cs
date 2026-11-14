using System;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Scheduling;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace SwapMid.MessageBus;

/// <summary>
/// 消息投递记录清理处理器：删除超过保留期且已终态（Delivered / DeadLettered）的 <see cref="MessageRecord"/>。
/// 保留天数取配置 MessageRecordCleanup:RetainDays（缺省 90）。
/// 🔴 故意不像 <see cref="ExecutionHistoryCleanupHandler"/>/<see cref="AuditLogCleanupHandler"/> 那样纯按
/// 年龄删：<c>MessageRecordAppService.ReplayAsync</c> 可把任意记录（含已死信的）重置回 Pending 重新投递，
/// 且重置不更新 CreationTime——若只按年龄删，一条 95 天前发布、5 分钟前刚被人工重推正在重试中的记录会被
/// 物理删除，摧毁投递中记录及其重试明细。故额外要求 Status 为终态才纳入候选；非终态（Pending/Failed）
/// 永不清理——单条 attempt 超限会自动升级为 DeadLettered，这类记录本不会长期停留在非终态，代价可忽略。
/// <see cref="MessageDeliveryAttempt"/> 子表由数据库 FK ON DELETE CASCADE 级联清除（同 AuditLogCleanup
/// 子表处理手法），本 handler 不单独清理它。
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler), typeof(MessageRecordCleanupHandler))]
public class MessageRecordCleanupHandler : IScheduledJobHandler, ITransientDependency
{
    public string JobKey => ScheduledJobKeys.MessageRecordCleanup;

    private readonly IRepository<MessageRecord, Guid> _repository;
    private readonly IConfiguration _configuration;
    private readonly IClock _clock;

    public MessageRecordCleanupHandler(
        IRepository<MessageRecord, Guid> repository,
        IConfiguration configuration,
        IClock clock)
    {
        _repository = repository;
        _configuration = configuration;
        _clock = clock;
    }

    public async Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken = default)
    {
        var retainDays = _configuration.GetValue("MessageRecordCleanup:RetainDays", 90);
        if (retainDays < 1)
        {
            return JobRunResult.Fail($"保留天数 {retainDays} 不合法（须 ≥ 1）");
        }

        // CreationTime 由 ABP 审计拦截器用注册的 IClock 写入；本平台未配 AbpClockOptions.Kind=Utc，
        // 与 ExecutionHistoryCleanup 的 StartedAt 同一本地时间基准，cutoff 须同基准避免时区偏差多删/少删。
        var cutoff = _clock.Now.AddDays(-retainDays);
        var count = await _repository.CountAsync(
            x => x.CreationTime < cutoff && (x.Status == MessageStatus.Delivered || x.Status == MessageStatus.DeadLettered),
            cancellationToken);

        if (context.IsDryRun)
        {
            return JobRunResult.Ok($"演练：将清理 {count} 条早于 {cutoff:yyyy-MM-dd} 的终态（已投递/已死信）消息记录（未删除）");
        }

        if (count > 0)
        {
            await _repository.DeleteDirectAsync(
                x => x.CreationTime < cutoff && (x.Status == MessageStatus.Delivered || x.Status == MessageStatus.DeadLettered),
                cancellationToken);
        }

        return JobRunResult.Ok($"已清理 {count} 条早于 {cutoff:yyyy-MM-dd} 的终态消息记录");
    }
}
