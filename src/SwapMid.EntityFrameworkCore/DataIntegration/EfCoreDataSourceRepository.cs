using System;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.DataIntegration;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore.DataIntegration;

public class EfCoreDataSourceRepository :
    EfCoreRepository<SwapMidDbContext, DataSource, Guid>,
    IDataSourceRepository
{
    public EfCoreDataSourceRepository(IDbContextProvider<SwapMidDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    public async Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AnyAsync(
            x => x.Name == name && (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken);
    }

    public async Task<DataSource> GetAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(typeof(DataSource), id);
    }
}
