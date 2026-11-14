using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.EnterpriseManagement;

public interface ICompanyDepartmentRepository : IRepository<CompanyDepartment, Guid>
{
    Task<bool> CodeExistsAsync(Guid enterpriseId, string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>取某部门自身 + 其全部下级（递归）的 Id 列表，用于"按部门查询含下级"。含防环。</summary>
    Task<List<Guid>> GetSelfAndDescendantIdsAsync(Guid rootId, CancellationToken cancellationToken = default);
}
