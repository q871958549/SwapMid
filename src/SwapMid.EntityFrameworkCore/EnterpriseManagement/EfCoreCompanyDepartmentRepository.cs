using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.EnterpriseManagement;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore.EnterpriseManagement;

public class EfCoreCompanyDepartmentRepository :
    EfCoreRepository<SwapMidDbContext, CompanyDepartment, Guid>,
    ICompanyDepartmentRepository
{
    public EfCoreCompanyDepartmentRepository(IDbContextProvider<SwapMidDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<bool> CodeExistsAsync(Guid enterpriseId, string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AnyAsync(
            x => x.EnterpriseId == enterpriseId && x.Code == code && (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken);
    }

    public async Task<List<Guid>> GetSelfAndDescendantIdsAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        // 部门量小（数百级），载入 (Id, ParentId) 全量后在内存 BFS 建树取子孙，
        // 规避原生递归 CTE 对物理表名/标识符引用/方言的依赖；HashSet 防环。
        var dbSet = await GetDbSetAsync();
        var pairs = await dbSet.Select(x => new { x.Id, x.ParentId }).ToListAsync(cancellationToken);
        var childrenByParent = pairs
            .Where(p => p.ParentId.HasValue)
            .ToLookup(p => p.ParentId!.Value, p => p.Id);

        var result = new List<Guid>();
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            result.Add(current);
            foreach (var child in childrenByParent[current])
            {
                queue.Enqueue(child);
            }
        }

        return result;
    }
}
