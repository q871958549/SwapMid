using System;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.EnterpriseManagement;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore.EnterpriseManagement;

public class EfCoreEnterpriseRepository :
    EfCoreRepository<SwapMidDbContext, Enterprise, Guid>,
    IEnterpriseRepository
{
    public EfCoreEnterpriseRepository(IDbContextProvider<SwapMidDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AnyAsync(
            x => x.Code == code && (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken);
    }
}
