using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.ApiManagement;
using SwapMid.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore.ApiManagement;

public class EfCoreApiRouteRepository :
    EfCoreRepository<SwapMidDbContext, ApiRoute, Guid>,
    IApiRouteRepository
{
    public EfCoreApiRouteRepository(IDbContextProvider<SwapMidDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<bool> PathAndMethodExistsAsync(
        string path, HttpMethodType httpMethod, Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AnyAsync(
            x => x.Path == path && x.HttpMethod == httpMethod &&
                 (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken);
    }

    public async Task<ApiRoute?> FindEnabledMatchAsync(
        string path, HttpMethodType httpMethod, CancellationToken cancellationToken = default)
    {
        var normalized = (path ?? string.Empty).ToLower();
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(x => x.IsEnabled
                && x.Path.ToLower() == normalized
                && (x.HttpMethod == HttpMethodType.ALL || x.HttpMethod == httpMethod))
            .OrderBy(x => x.Order)
            .ThenBy(x => x.HttpMethod)   // 具体方法(枚举值 < ALL=99)优先于 ALL 通配
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<ApiRoute>> GetListAsync(
        string? filter = null, Guid? groupId = null, bool? isEnabled = null,
        DateTime? startTime = null, DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        string? sorting = null, int maxResultCount = 10, int skipCount = 0,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        var query = BuildQuery(dbSet, filter, groupId, isEnabled)
            .ApplyTimeRange(startTime, endTime, timeType);

        // appendIdTiebreaker: false —— 本调用点在返回值上自带 ThenBy(Name) 兜底，
        // 让共享件追加的 Id 排在它之前会把 Name 兜底整个短路（见该参数注释）。
        query = query
            .ApplyWhitelistedSorting(sorting, SortWhitelist, "order", appendIdTiebreaker: false)
            .ThenBy(x => x.Name);

        return await query.Skip(skipCount).Take(maxResultCount).ToListAsync(cancellationToken);
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）：未命中回退 order（默认排序序号）升序，杜绝把用户串
    // 裸传给动态排序（排序注入）。groupName 是 AppService 层分页后按 GroupId 查字典回填的派生列（非本
    // 仓储持久化列，需 join），不进白名单——前端对应列已加 enableSorting: false。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<ApiRoute>, bool, IOrderedQueryable<ApiRoute>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<ApiRoute>, bool, IOrderedQueryable<ApiRoute>>>
        {
            ["order"] = (q, d) => d ? q.OrderByDescending(x => x.Order) : q.OrderBy(x => x.Order),
            ["name"] = (q, d) => d ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name),
            ["path"] = (q, d) => d ? q.OrderByDescending(x => x.Path) : q.OrderBy(x => x.Path),
            ["httpmethod"] = (q, d) => d ? q.OrderByDescending(x => x.HttpMethod) : q.OrderBy(x => x.HttpMethod),
            ["upstreamurl"] = (q, d) => d ? q.OrderByDescending(x => x.UpstreamUrl) : q.OrderBy(x => x.UpstreamUrl),
            ["isenabled"] = (q, d) => d ? q.OrderByDescending(x => x.IsEnabled) : q.OrderBy(x => x.IsEnabled),
            // 审计列必须在白名单：前端 withAuditColumns 会自动追加创建/修改时间列且默认可排序，缺键 = 静默回退。
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
            ["lastmodificationtime"] = (q, d) => d ? q.OrderByDescending(x => x.LastModificationTime) : q.OrderBy(x => x.LastModificationTime),
        };

    public async Task<int> GetCountAsync(
        string? filter = null, Guid? groupId = null, bool? isEnabled = null,
        DateTime? startTime = null, DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await BuildQuery(dbSet, filter, groupId, isEnabled)
            .ApplyTimeRange(startTime, endTime, timeType)
            .CountAsync(cancellationToken);
    }

    private static IQueryable<ApiRoute> BuildQuery(
        IQueryable<ApiRoute> query, string? filter, Guid? groupId, bool? isEnabled)
    {
        if (!string.IsNullOrWhiteSpace(filter))
            query = query.Where(x =>
                x.Name.Contains(filter) ||
                x.Path.Contains(filter) ||
                x.UpstreamUrl.Contains(filter));

        if (groupId.HasValue)
            query = query.Where(x => x.GroupId == groupId);

        if (isEnabled.HasValue)
            query = query.Where(x => x.IsEnabled == isEnabled.Value);

        return query;
    }
}
