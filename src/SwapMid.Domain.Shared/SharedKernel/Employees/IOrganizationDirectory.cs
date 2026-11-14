using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.SharedKernel.Employees;

/// <summary>企业、部门与岗位的只读目录。实现在企业管理域内。</summary>
public interface IOrganizationDirectory
{
    Task<DepartmentSnapshot?> FindDepartmentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<DepartmentSnapshot>> GetDepartmentListAsync(CancellationToken cancellationToken = default);
    Task<List<Guid>> GetSelfAndDescendantDepartmentIdsAsync(Guid rootId, CancellationToken cancellationToken = default);
    Task<string?> GetEnterpriseNameAsync(Guid id, CancellationToken cancellationToken = default);
    Task<string?> GetPositionNameAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, string>> GetEnterpriseNamesAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, string>> GetPositionNamesAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default);
}

/// <summary>部门主数据只读快照。</summary>
public sealed record DepartmentSnapshot(
    Guid Id,
    Guid EnterpriseId,
    Guid? ParentId,
    string Name,
    Guid? LeaderEmployeeId);
