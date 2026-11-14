using System;
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
}
