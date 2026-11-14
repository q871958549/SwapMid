using System;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Notification;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace SwapMid.Scheduling;

/// <summary>
/// 通知发送日志清理处理器：删除超过保留期的 <see cref="NotificationLog"/>（通用通知中心的发送台账，
/// 每次发送每渠道落一条，随告警/提醒任务时间正比无界增长）。保留天数取配置
/// NotificationLogCleanup:RetainDays（缺省 90）。演练=只统计将删条数、不删；正式=删除并回报条数。
/// 🔴 与 <c>MessageRecordCleanupHandler</c> 的终态过滤**故意不同**：<see cref="NotificationLog"/>
/// 没有状态机，是发送完成后一次性写入的只读台账（<c>NotificationLogAppService</c> 只有 GetListAsync，
/// 平台不提供重发/重置入口），落库即终态——不存在「95 天前的旧记录被人工重推、正在重投中」那种误删风险，
/// 故按年龄一刀切，形态同 <see cref="AuditLogCleanupHandler"/>/<see cref="ExecutionHistoryCleanupHandler"/>。
/// 同理**不按 <c>Success</c> 过滤**：失败记录同样是一次性历史台账、同样无界增长，只保留成功记录会让失败行永久堆积；
/// 失败详情的排查价值由保留期本身（缺省 90 天）承载，需要留更久就调大 RetainDays。
/// </summary>
// 必须显式暴露 IScheduledJobHandler：ABP 默认约定不暴露非命名匹配接口，漏标则该 handler 收不进
// ScheduledJobHandlerCatalog、Runner 按 JobKey 找不到它，任务静默不执行（见 Scheduling/CLAUDE.md 双列铁律）。
[ExposeServices(typeof(IScheduledJobHandler), typeof(NotificationLogCleanupHandler))]
public class NotificationLogCleanupHandler : IScheduledJobHandler, ITransientDependency
{
    public string JobKey => ScheduledJobKeys.NotificationLogCleanup;

    private readonly IRepository<NotificationLog, Guid> _repository;
    private readonly IConfiguration _configuration;
    private readonly IClock _clock;

    public NotificationLogCleanupHandler(
        IRepository<NotificationLog, Guid> repository,
        IConfiguration configuration,
        IClock clock)
    {
        _repository = repository;
        _configuration = configuration;
        _clock = clock;
    }

    public async Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken = default)
    {
        var retainDays = _configuration.GetValue("NotificationLogCleanup:RetainDays", 90);
        if (retainDays < 1)
        {
            return JobRunResult.Fail($"保留天数 {retainDays} 不合法（须 ≥ 1）");
        }

        // CreationTime 由 ABP 审计拦截器用注册的 IClock 写入；本平台未配 AbpClockOptions.Kind=Utc，
        // 与 MessageRecordCleanup 同一本地时间基准，cutoff 须同基准避免时区偏差多删/少删
        // （AuditLogCleanup 用 UtcNow 是因为 AbpAuditLogs.ExecutionTime 是 ABP 标准 UTC，勿照抄）。
        var cutoff = _clock.Now.AddDays(-retainDays);
        var count = await _repository.CountAsync(x => x.CreationTime < cutoff, cancellationToken);

        if (context.IsDryRun)
        {
            return JobRunResult.Ok($"演练：将清理 {count} 条早于 {cutoff:yyyy-MM-dd} 的通知发送日志（未删除）");
        }

        if (count > 0)
        {
            // DeleteDirectAsync → EF Core ExecuteDeleteAsync（set-based，不加载实体）；
            // NotificationLogs 表 CreationTime 上有索引（NotificationEntityTypeConfigurations），删除走索引。
            await _repository.DeleteDirectAsync(x => x.CreationTime < cutoff, cancellationToken);
        }

        return JobRunResult.Ok($"已清理 {count} 条早于 {cutoff:yyyy-MM-dd} 的通知发送日志");
    }
}
