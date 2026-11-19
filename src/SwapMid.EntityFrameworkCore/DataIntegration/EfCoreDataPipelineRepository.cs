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

public class EfCoreDataPipelineRepository :
    EfCoreRepository<SwapMidDbContext, DataPipeline, Guid>,
    IDataPipelineRepository
{
    public EfCoreDataPipelineRepository(IDbContextProvider<SwapMidDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    public async Task<DataPipelineActivationLock> GetActivationForUpdateAsync(
        Guid pipelineId,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var provider = ResolveActivationLockProvider(
            dbContext.Database.ProviderName);
        if (provider == ActivationLockProvider.PostgreSql &&
            dbContext.Database.CurrentTransaction == null)
        {
            // FOR UPDATE 只有在显式事务内才会跨语句持有。必须在任何读取前失败关闭，
            // 避免把一次普通读取误当作完成了激活串行化。
            throw new InvalidOperationException(
                "Data pipeline activation row locking requires an active transaction.");
        }

        var pipelines = dbContext.Set<DataPipeline>();
        var dataSources = dbContext.Set<DataSource>();

        // 锁顺序的第一层固定为 pipeline。只有拿到该行锁后才读取其当前 source/target，
        // 因而并发改绑数据源无法落在“读外部证明后、提交前”的窗口中。
        var pipeline = provider == ActivationLockProvider.PostgreSql
            ? await pipelines
                .FromSqlRaw(
                    """
                    SELECT *
                    FROM "AppDataPipelines"
                    WHERE "Id" = {0}
                    FOR UPDATE
                    """,
                    pipelineId)
                .SingleOrDefaultAsync(cancellationToken)
            : await pipelines.SingleOrDefaultAsync(
                item => item.Id == pipelineId,
                cancellationToken);
        if (pipeline == null)
        {
            throw new EntityNotFoundException(
                typeof(DataPipeline),
                pipelineId);
        }

        var lockedDataSources = new Dictionary<Guid, DataSource>();
        foreach (var dataSourceId in BuildActivationDataSourceLockOrder(
                     pipeline.SourceId,
                     pipeline.TargetId))
        {
            var dataSource = provider == ActivationLockProvider.PostgreSql
                ? await dataSources
                    .FromSqlRaw(
                        """
                        SELECT *
                        FROM "AppDataSources"
                        WHERE "Id" = {0}
                        FOR UPDATE
                        """,
                        dataSourceId)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(cancellationToken)
                : await dataSources
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.Id == dataSourceId,
                        cancellationToken);
            if (dataSource == null)
            {
                throw new EntityNotFoundException(
                    typeof(DataSource),
                    dataSourceId);
            }

            lockedDataSources.Add(dataSourceId, dataSource);
        }

        return new DataPipelineActivationLock(
            pipeline,
            lockedDataSources[pipeline.SourceId],
            pipeline.TargetId.HasValue
                ? lockedDataSources[pipeline.TargetId.Value]
                : null);
    }

    internal static IReadOnlyList<Guid> BuildActivationDataSourceLockOrder(
        Guid sourceId,
        Guid? targetId)
    {
        if (!targetId.HasValue || targetId.Value == sourceId)
        {
            return new[] { sourceId };
        }

        return new[] { sourceId, targetId.Value }
            .OrderBy(id => id)
            .ToArray();
    }

    internal static ActivationLockProvider ResolveActivationLockProvider(
        string? providerName)
    {
        if (string.Equals(
                providerName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
        {
            return ActivationLockProvider.PostgreSql;
        }

        if (string.Equals(
                providerName,
                "Microsoft.EntityFrameworkCore.Sqlite",
                StringComparison.Ordinal))
        {
            // SQLite 不支持 SELECT FOR UPDATE；该分支只用于共享测试宿主验证
            // 协调器的读取/校验/更新闭环，不代表生产并发语义。
            return ActivationLockProvider.SqliteTestSeam;
        }

        throw new InvalidOperationException(
            "Data pipeline activation row locking requires PostgreSQL.");
    }

    public async Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AnyAsync(
            x => x.Name == name && (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken);
    }

    public async Task<List<DataPipeline>> GetListAsync(
        string? filter = null, Guid? sourceId = null, PipelineStatus? status = null,
        DateTime? startTime = null, DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        string? sorting = null, int maxResultCount = 10, int skipCount = 0,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        var query = BuildQuery(dbSet, filter, sourceId, status)
            .ApplyTimeRange(startTime, endTime, timeType)
            .ApplyWhitelistedSorting(sorting, SortWhitelist, "creationtime", defaultDescending: true);
        return await query.Skip(skipCount).Take(maxResultCount).ToListAsync(cancellationToken);
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）。原实现是「有排序参数就一律按名称升序」的伪排序：
    // 用户点「创建时间降序」实际得到「名称升序」，比完全忽略排序更迷惑。
    // 关联名列 sourceName / targetName 需 join，不在白名单（前端对应列已禁排序）。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<DataPipeline>, bool, IOrderedQueryable<DataPipeline>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<DataPipeline>, bool, IOrderedQueryable<DataPipeline>>>
        {
            ["name"] = (q, d) => d ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name),
            ["status"] = (q, d) => d ? q.OrderByDescending(x => x.Status) : q.OrderBy(x => x.Status),
            ["schedulecron"] = (q, d) => d ? q.OrderByDescending(x => x.ScheduleCron) : q.OrderBy(x => x.ScheduleCron),
            ["lastrunat"] = (q, d) => d ? q.OrderByDescending(x => x.LastRunAt) : q.OrderBy(x => x.LastRunAt),
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
            ["lastmodificationtime"] = (q, d) => d ? q.OrderByDescending(x => x.LastModificationTime) : q.OrderBy(x => x.LastModificationTime),
        };

    public async Task<int> GetCountAsync(
        string? filter = null, Guid? sourceId = null, PipelineStatus? status = null,
        DateTime? startTime = null, DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await BuildQuery(dbSet, filter, sourceId, status)
            .ApplyTimeRange(startTime, endTime, timeType)
            .CountAsync(cancellationToken);
    }

    private static IQueryable<DataPipeline> BuildQuery(
        IQueryable<DataPipeline> query, string? filter, Guid? sourceId, PipelineStatus? status)
    {
        if (!string.IsNullOrWhiteSpace(filter))
            query = query.Where(x => x.Name.Contains(filter));
        if (sourceId.HasValue)
            query = query.Where(x => x.SourceId == sourceId.Value);
        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);
        return query;
    }

    internal enum ActivationLockProvider
    {
        PostgreSql,
        SqliteTestSeam,
    }
}
