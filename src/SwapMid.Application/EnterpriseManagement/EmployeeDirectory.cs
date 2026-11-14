using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.SharedKernel.Employees;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Uow;

namespace SwapMid.EnterpriseManagement;

/// <summary>把 Employee 聚合投影为跨域可用的不可变快照。</summary>
[ExposeServices(typeof(IEmployeeDirectory))]
public class EmployeeDirectory : IEmployeeDirectory, ITransientDependency
{
    private readonly IRepository<Employee, Guid> _employeeRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public EmployeeDirectory(
        IRepository<Employee, Guid> employeeRepository,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _employeeRepository = employeeRepository;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<EmployeeSnapshot?> FindAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.FindAsync(id, cancellationToken: cancellationToken);
        return employee == null ? null : ToSnapshot(employee);
    }

    public async Task<List<EmployeeSnapshot>> GetListAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        return await QueryWithAmbientOrNewUowAsync(async ct =>
        {
            var query = await _employeeRepository.GetQueryableAsync();
            var employees = await _asyncExecuter.ToListAsync(
                query.Where(employee => idList.Contains(employee.Id)),
                ct);
            return employees.Select(ToSnapshot).ToList();
        }, cancellationToken);
    }

    public async Task<EmployeeSnapshot?> FindByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.FirstOrDefaultAsync(
            item => item.UserId == userId,
            cancellationToken: cancellationToken);
        return employee == null ? null : ToSnapshot(employee);
    }

    public async Task<List<EmployeeSnapshot>> GetListByUserIdsAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        var idList = userIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        return await QueryWithAmbientOrNewUowAsync(async ct =>
        {
            var query = await _employeeRepository.GetQueryableAsync();
            var employees = await _asyncExecuter.ToListAsync(
                query.Where(employee => employee.UserId.HasValue && idList.Contains(employee.UserId.Value)),
                ct);
            return employees.Select(ToSnapshot).ToList();
        }, cancellationToken);
    }

    public async Task<EmployeeSnapshot?> FindByEmployeeNoAsync(
        string employeeNo,
        CancellationToken cancellationToken = default)
    {
        var matches = await GetListByEmployeeNosAsync([employeeNo], cancellationToken);
        return matches.FirstOrDefault();
    }

    public async Task<List<EmployeeSnapshot>> GetListByEmployeeNosAsync(
        IEnumerable<string> employeeNos,
        CancellationToken cancellationToken = default)
    {
        var lowered = employeeNos
            .SelectMany(EmployeeNoLookup.Expand)
            .Select(item => item.ToLowerInvariant())
            .Distinct()
            .ToList();
        if (lowered.Count == 0)
        {
            return [];
        }

        // 指标适用人员含已离职/被手工删掉的工号：软删过滤会让名片只剩工号。对照键大小写不敏感，并兼容有无 SM 前缀。
        return await QueryWithAmbientOrNewUowAsync(async ct =>
        {
            using (_dataFilter.Disable<ISoftDelete>())
            {
                var query = await _employeeRepository.GetQueryableAsync();
                var employees = await _asyncExecuter.ToListAsync(
                    query.Where(employee => lowered.Contains(employee.EmployeeNo.ToLower())),
                    ct);
                return employees.Select(ToSnapshot).ToList();
            }
        }, cancellationToken);
    }

    public Task<List<EmployeeSnapshot>> GetActiveListAsync(
        Guid? departmentId = null,
        CancellationToken cancellationToken = default)
        => QueryWithAmbientOrNewUowAsync(async ct =>
        {
            var query = (await _employeeRepository.GetQueryableAsync())
                .Where(employee => employee.Status == EmployeeStatus.Active);
            if (departmentId.HasValue)
            {
                query = query.Where(employee => employee.DepartmentId == departmentId.Value);
            }

            var employees = await _asyncExecuter.ToListAsync(
                query.OrderBy(employee => employee.EmployeeNo),
                ct);
            return employees.Select(ToSnapshot).ToList();
        }, cancellationToken);

    public Task<List<EmployeeSnapshot>> GetAllListAsync(
        CancellationToken cancellationToken = default)
        => QueryWithAmbientOrNewUowAsync(async ct =>
        {
            var query = await _employeeRepository.GetQueryableAsync();
            var employees = await _asyncExecuter.ToListAsync(
                query.OrderBy(employee => employee.EmployeeNo),
                ct);
            return employees.Select(ToSnapshot).ToList();
        }, cancellationToken);

    public async Task<List<Guid>> GetBoundUserIdsByDepartmentIdsAsync(
        IEnumerable<Guid> departmentIds,
        CancellationToken cancellationToken = default)
    {
        var departmentIdList = departmentIds.Distinct().ToList();
        if (departmentIdList.Count == 0)
        {
            return [];
        }

        return await QueryWithAmbientOrNewUowAsync(async ct =>
        {
            var query = await _employeeRepository.GetQueryableAsync();
            return await _asyncExecuter.ToListAsync(
                query
                    .Where(employee => employee.UserId.HasValue
                        && employee.DepartmentId.HasValue
                        && departmentIdList.Contains(employee.DepartmentId.Value))
                    .Select(employee => employee.UserId!.Value)
                    .Distinct(),
                ct);
        }, cancellationToken);
    }

    public Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
        => _employeeRepository.CountAsync(
            employee => employee.Status == EmployeeStatus.Active,
            cancellationToken);

    /// <summary>
    /// Hangfire <c>UseUnitOfWork=false</c> 等无 ambient UoW 的后台入口下，
    /// <see cref="IRepository{TEntity,TKey}.GetQueryableAsync"/> 会在无 ambient 时立刻释放
    /// DbContext，随后 <c>ToListAsync</c> 抛 <see cref="ObjectDisposedException"/>。
    /// 有 ambient 则复用，避免嵌套短事务读不到调用方未提交行。
    /// </summary>
    private async Task<T> QueryWithAmbientOrNewUowAsync<T>(
        Func<CancellationToken, Task<T>> query,
        CancellationToken cancellationToken)
    {
        if (_unitOfWorkManager.Current != null)
        {
            return await query(cancellationToken);
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var result = await query(cancellationToken);
        await uow.CompleteAsync(cancellationToken);
        return result;
    }

    private static EmployeeSnapshot ToSnapshot(Employee employee)
        => new(
            employee.Id,
            employee.EnterpriseId,
            employee.DepartmentId,
            employee.PositionId,
            employee.UserId,
            employee.EmployeeNo,
            employee.Name,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.Status);
}
