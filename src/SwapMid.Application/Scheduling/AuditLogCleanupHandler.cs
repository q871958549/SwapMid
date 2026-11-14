using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp.AuditLogging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.Scheduling;

/// <summary>
/// 审计日志清理处理器（第 3 个 <see cref="IScheduledJobHandler"/> 接入方）：删除超保留期的 AbpAuditLogs，
/// 子表（AuditLogActions / EntityChanges / EntityPropertyChanges）由数据库 FK ON DELETE CASCADE 级联清除。
/// 保留天数取 AuditLogCleanup:RetainDays（缺省 90）。演练=只统计将删条数、不删；正式=删除。
/// ⚠️ cutoff 用 <c>UtcNow</c>：<c>AuditLog.ExecutionTime</c> 是 ABP 标准 UTC，与执行历史清理用 <c>IClock.Now</c>
/// （本地时间）不同，勿混。迁自原「直接删」的 AuditLogCleanupJob，接入 Runner 后获得演练预览删除量
/// （首次开启前先预览积压量，避免单次删除过多导致长事务/锁）。
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler), typeof(AuditLogCleanupHandler))]
public class AuditLogCleanupHandler : IScheduledJobHandler, ITransientDependency
{
    public string JobKey => ScheduledJobKeys.AuditLogCleanup;

    private readonly IRepository<AuditLog, Guid> _auditLogRepository;
    private readonly IConfiguration _configuration;

    public AuditLogCleanupHandler(
        IRepository<AuditLog, Guid> auditLogRepository,
        IConfiguration configuration)
    {
        _auditLogRepository = auditLogRepository;
        _configuration = configuration;
    }

    public async Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken = default)
    {
        var retainDays = _configuration.GetValue("AuditLogCleanup:RetainDays", 90);
        if (retainDays < 1)
        {
            return JobRunResult.Fail($"保留天数 {retainDays} 不合法（须 ≥ 1）");
        }

        // ExecutionTime 为 UTC（ABP 标准），cutoff 用 UtcNow，避免本地时区导致多删/少删。
        var cutoff = DateTime.UtcNow.AddDays(-retainDays);
        var count = await _auditLogRepository.CountAsync(x => x.ExecutionTime < cutoff, cancellationToken);

        if (context.IsDryRun)
        {
            return JobRunResult.Ok($"演练：将清理 {count} 条早于 {cutoff:yyyy-MM-dd} 的审计日志（未删除）");
        }

        if (count > 0)
        {
            // DeleteDirectAsync → EF Core ExecuteDeleteAsync（set-based，不加载实体）；子表由 FK 级联清除。
            await _auditLogRepository.DeleteDirectAsync(x => x.ExecutionTime < cutoff, cancellationToken);
        }

        return JobRunResult.Ok($"已清理 {count} 条早于 {cutoff:yyyy-MM-dd} 的审计日志");
    }
}
