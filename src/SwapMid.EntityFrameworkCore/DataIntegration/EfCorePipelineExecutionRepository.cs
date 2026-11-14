using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.DataIntegration;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore.DataIntegration;

public class EfCorePipelineExecutionRepository :
    EfCoreRepository<SwapMidDbContext, PipelineExecution, Guid>,
    IPipelineExecutionRepository
{
    public EfCorePipelineExecutionRepository(IDbContextProvider<SwapMidDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    public async Task<List<PipelineExecution>> GetListAsync(
        Guid? pipelineId = null, ExecutionStatus? status = null,
        DateTime? startTime = null, DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.StartedAt,
        string? sorting = null, int maxResultCount = 20, int skipCount = 0,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        var query = BuildQuery(dbSet, pipelineId, status, startTime, endTime, timeType);
        // ⚠️ defaultDescending: true 不可省（原为 OrderByDescending(StartedAt)）。
        // 此前本方法签名收下 sorting 参数却直接丢弃——前端执行历史弹窗甚至已显式传 'StartedAt desc'，
        // 一直被无视；接上白名单后各列头即刻生效。
        query = query.ApplyWhitelistedSorting(sorting, SortWhitelist, "startedat", defaultDescending: true);
        return await query.Skip(skipCount).Take(maxResultCount).ToListAsync(cancellationToken);
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）。ErrorMessage 故意不在列：按本域规范它禁止回显，
    // 允许按它排序同样会泄露内容顺序。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<PipelineExecution>, bool, IOrderedQueryable<PipelineExecution>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<PipelineExecution>, bool, IOrderedQueryable<PipelineExecution>>>
        {
            ["startedat"] = (q, d) => d ? q.OrderByDescending(x => x.StartedAt) : q.OrderBy(x => x.StartedAt),
            ["finishedat"] = (q, d) => d ? q.OrderByDescending(x => x.FinishedAt) : q.OrderBy(x => x.FinishedAt),
            ["pipelinename"] = (q, d) => d ? q.OrderByDescending(x => x.PipelineName) : q.OrderBy(x => x.PipelineName),
            ["status"] = (q, d) => d ? q.OrderByDescending(x => x.Status) : q.OrderBy(x => x.Status),
            ["triggertype"] = (q, d) => d ? q.OrderByDescending(x => x.TriggerType) : q.OrderBy(x => x.TriggerType),
            ["processedrows"] = (q, d) => d ? q.OrderByDescending(x => x.ProcessedRows) : q.OrderBy(x => x.ProcessedRows),
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
        };

    public async Task<int> GetCountAsync(
        Guid? pipelineId = null, ExecutionStatus? status = null,
        DateTime? startTime = null, DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.StartedAt,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await BuildQuery(dbSet, pipelineId, status, startTime, endTime, timeType).CountAsync(cancellationToken);
    }

    public async Task<List<PipelineExecution>> GetDurableStatesAsync(
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await BuildDurableStatesQuery(dbSet, maxResultCount)
            .ToListAsync(cancellationToken);
    }

    internal static IQueryable<PipelineExecution> BuildDurableStatesQuery(
        IQueryable<PipelineExecution> query,
        int maxResultCount)
        => query
            .Where(execution =>
                execution.Status == ExecutionStatus.Running ||
                execution.Status == ExecutionStatus.OutcomeUnknown)
            .OrderBy(execution => execution.StartedAt)
            .ThenBy(execution => execution.Id)
            .Take(maxResultCount);

    public async Task<PipelineExecution> GetForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var dbSet = await GetDbSetAsync();
        if (string.Equals(
                dbContext.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.Sqlite",
                StringComparison.Ordinal))
        {
            // 仅供共享 SQLite 测试宿主验证协议编排；SQLite 不支持 SELECT FOR UPDATE，
            // 此分支不证明生产并发语义。真实行锁与 ACK 丢失契约必须由 PostgreSQL 18
            // 集成测试覆盖。
            return await dbSet.SingleOrDefaultAsync(
                       execution => execution.Id == id,
                       cancellationToken) ??
                   throw new EntityNotFoundException(
                       typeof(PipelineExecution),
                       id);
        }

        if (!string.Equals(
                dbContext.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Pipeline recovery row locking requires PostgreSQL.");
        }

        if (dbContext.Database.CurrentTransaction == null)
        {
            // FOR UPDATE 只有在显式事务内才跨语句持有；未来调用方若漏开事务，
            // 必须在读行前失败关闭，不能给上层“已经串行化”的错觉。
            throw new InvalidOperationException(
                "Pipeline recovery row locking requires an active transaction.");
        }

        var execution = await dbSet
            .FromSqlRaw(
                """
                SELECT *
                FROM "AppPipelineExecutions"
                WHERE "Id" = {0}
                FOR UPDATE
                """,
                id)
            .SingleOrDefaultAsync(cancellationToken);
        return execution ?? throw new EntityNotFoundException(
            typeof(PipelineExecution),
            id);
    }

    private static IQueryable<PipelineExecution> BuildQuery(
        IQueryable<PipelineExecution> query,
        Guid? pipelineId, ExecutionStatus? status,
        DateTime? startTime, DateTime? endTime,
        ListQueryTimeType timeType)
    {
        if (pipelineId.HasValue) query = query.Where(x => x.PipelineId == pipelineId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return query.ApplyTimeRange(startTime, endTime, timeType, ListQueryTimeType.StartedAt);
    }
}
