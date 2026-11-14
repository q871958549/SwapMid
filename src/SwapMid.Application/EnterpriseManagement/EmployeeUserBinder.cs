using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.SharedKernel.Employees;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace SwapMid.EnterpriseManagement;

/// <summary>员工与账号绑定写入口；所有写操作仍经 Employee 聚合方法完成。</summary>
[ExposeServices(typeof(IEmployeeUserBinder))]
public class EmployeeUserBinder : IEmployeeUserBinder, ITransientDependency
{
    private readonly IRepository<Employee, Guid> _employeeRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public EmployeeUserBinder(
        IRepository<Employee, Guid> employeeRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _employeeRepository = employeeRepository;
        _asyncExecuter = asyncExecuter;
    }

    public async Task BindAsync(
        Guid employeeId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetAsync(employeeId, cancellationToken: cancellationToken);
        employee.BindUser(userId);
        await _employeeRepository.UpdateAsync(employee, cancellationToken: cancellationToken);
    }

    public async Task UnbindAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetAsync(employeeId, cancellationToken: cancellationToken);
        if (!employee.UserId.HasValue)
        {
            return;
        }

        employee.UnbindUser();
        await _employeeRepository.UpdateAsync(employee, cancellationToken: cancellationToken);
    }

    public async Task UnbindAllByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var query = await _employeeRepository.GetQueryableAsync();
        var employees = await _asyncExecuter.ToListAsync(
            query.Where(employee => employee.UserId == userId),
            cancellationToken);
        foreach (var employee in employees)
        {
            employee.UnbindUser();
            await _employeeRepository.UpdateAsync(employee, cancellationToken: cancellationToken);
        }
    }
}
