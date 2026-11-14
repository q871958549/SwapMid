using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.SharedKernel.Employees;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace SwapMid.EnterpriseManagement;

/// <summary>企业、部门和岗位的跨域只读投影。</summary>
[ExposeServices(typeof(IOrganizationDirectory))]
public class OrganizationDirectory : IOrganizationDirectory, ITransientDependency
{
    private readonly ICompanyDepartmentRepository _departmentRepository;
    private readonly IRepository<Enterprise, Guid> _enterpriseRepository;
    private readonly IRepository<Position, Guid> _positionRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public OrganizationDirectory(
        ICompanyDepartmentRepository departmentRepository,
        IRepository<Enterprise, Guid> enterpriseRepository,
        IRepository<Position, Guid> positionRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _departmentRepository = departmentRepository;
        _enterpriseRepository = enterpriseRepository;
        _positionRepository = positionRepository;
        _asyncExecuter = asyncExecuter;
    }

    public async Task<DepartmentSnapshot?> FindDepartmentAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var department = await _departmentRepository.FindAsync(id, cancellationToken: cancellationToken);
        return department == null ? null : ToSnapshot(department);
    }

    public async Task<List<DepartmentSnapshot>> GetDepartmentListAsync(
        CancellationToken cancellationToken = default)
    {
        var departments = await _departmentRepository.GetListAsync(cancellationToken: cancellationToken);
        return departments.Select(ToSnapshot).ToList();
    }

    public Task<List<Guid>> GetSelfAndDescendantDepartmentIdsAsync(
        Guid rootId,
        CancellationToken cancellationToken = default)
        => _departmentRepository.GetSelfAndDescendantIdsAsync(rootId, cancellationToken);

    public async Task<string?> GetEnterpriseNameAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => (await _enterpriseRepository.FindAsync(id, cancellationToken: cancellationToken))?.Name;

    public async Task<string?> GetPositionNameAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => (await _positionRepository.FindAsync(id, cancellationToken: cancellationToken))?.Name;

    public async Task<IReadOnlyDictionary<Guid, string>> GetEnterpriseNamesAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var query = await _enterpriseRepository.GetQueryableAsync();
        return (await _asyncExecuter.ToListAsync(
                query.Where(item => idList.Contains(item.Id)),
                cancellationToken))
            .ToDictionary(item => item.Id, item => item.Name);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetPositionNamesAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var query = await _positionRepository.GetQueryableAsync();
        return (await _asyncExecuter.ToListAsync(
                query.Where(item => idList.Contains(item.Id)),
                cancellationToken))
            .ToDictionary(item => item.Id, item => item.Name);
    }

    private static DepartmentSnapshot ToSnapshot(CompanyDepartment department)
        => new(
            department.Id,
            department.EnterpriseId,
            department.ParentId,
            department.Name,
            department.LeaderEmployeeId);
}
