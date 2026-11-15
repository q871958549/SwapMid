using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SwapMid.DataIntegration;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore.DataIntegration;

public class EfCoreCdcStreamRepository :
    EfCoreRepository<SwapMidDbContext, CdcStreamDefinition, Guid>,
    ICdcStreamRepository
{
    public EfCoreCdcStreamRepository(IDbContextProvider<SwapMidDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<bool> NameExistsAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AnyAsync(
            x => x.Name == name && (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken);
    }

    public async Task<long> GetCountAsync(
        string? filter = null,
        Guid? sourceId = null,
        CdcStreamStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = await BuildFilteredQueryAsync(filter, sourceId, status);
        return await query.LongCountAsync(cancellationToken);
    }

    public async Task<List<CdcStreamDefinition>> GetListAsync(
        string? filter = null,
        Guid? sourceId = null,
        CdcStreamStatus? status = null,
        string? sorting = null,
        int maxResultCount = int.MaxValue,
        int skipCount = 0,
        CancellationToken cancellationToken = default)
    {
        var query = await BuildFilteredQueryAsync(filter, sourceId, status);
        query = ApplySorting(query, sorting);
        return await query
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<CdcStreamDefinition>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(x => x.Status == CdcStreamStatus.Active)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<IQueryable<CdcStreamDefinition>> BuildFilteredQueryAsync(
        string? filter,
        Guid? sourceId,
        CdcStreamStatus? status)
    {
        var query = (await GetDbSetAsync()).AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var term = filter.Trim();
            query = query.Where(x =>
                x.Name.Contains(term) ||
                x.PublicationName.Contains(term) ||
                x.SlotName.Contains(term) ||
                (x.MonitoredTables != null && x.MonitoredTables.Contains(term)) ||
                (x.TargetTopicName != null && x.TargetTopicName.Contains(term)));
        }

        if (sourceId.HasValue)
        {
            query = query.Where(x => x.SourceId == sourceId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        return query;
    }

    private static IQueryable<CdcStreamDefinition> ApplySorting(
        IQueryable<CdcStreamDefinition> query,
        string? sorting)
    {
        var whitelist = new Dictionary<string, Func<IQueryable<CdcStreamDefinition>, bool, IOrderedQueryable<CdcStreamDefinition>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = (q, desc) => desc ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name),
            ["status"] = (q, desc) => desc ? q.OrderByDescending(x => x.Status) : q.OrderBy(x => x.Status),
            ["lastcapturedat"] = (q, desc) => desc ? q.OrderByDescending(x => x.LastCapturedAt) : q.OrderBy(x => x.LastCapturedAt),
            ["totalcapturedcount"] = (q, desc) => desc ? q.OrderByDescending(x => x.TotalCapturedCount) : q.OrderBy(x => x.TotalCapturedCount),
            ["creationtime"] = (q, desc) => desc ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
        };

        return query.ApplyWhitelistedSorting(
            sorting,
            whitelist,
            defaultKey: "creationtime",
            defaultDescending: true);
    }
}
