using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.MessageBus;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore.MessageBus;

public class EfCoreMessageTopicRepository :
    EfCoreRepository<SwapMidDbContext, MessageTopic, Guid>,
    IMessageTopicRepository
{
    public EfCoreMessageTopicRepository(IDbContextProvider<SwapMidDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    public async Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AnyAsync(x => x.Name == name && (!excludeId.HasValue || x.Id != excludeId.Value), cancellationToken);
    }

    public async Task<MessageTopic?> FindNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<MessageTopic?> FindByNameNoTrackingAsync(string name, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.Name == name, cancellationToken);
    }

    public async Task<HashSet<Guid>> GetExistingIdsNoTrackingAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var dbSet = await GetDbSetAsync();
        var list = await dbSet.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        return list.ToHashSet();
    }
}
