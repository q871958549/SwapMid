using System;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.EnterpriseManagement;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore.EnterpriseManagement;

public class EfCoreEmployeeRepository :
    EfCoreRepository<SwapMidDbContext, Employee, Guid>,
    IEmployeeRepository
{
    public EfCoreEmployeeRepository(IDbContextProvider<SwapMidDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<bool> EmployeeNoExistsAsync(Guid enterpriseId, string employeeNo, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AnyAsync(
            x => x.EnterpriseId == enterpriseId && x.EmployeeNo == employeeNo && (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken);
    }

    public async Task<bool> UserBindingExistsAsync(Guid userId, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AnyAsync(
            x => x.UserId == userId && (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken);
    }
}
