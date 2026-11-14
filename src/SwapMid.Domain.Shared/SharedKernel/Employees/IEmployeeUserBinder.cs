using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.SharedKernel.Employees;

/// <summary>员工与账号绑定写契约。实现在企业管理域内，仅账号权限域消费。</summary>
public interface IEmployeeUserBinder
{
    Task BindAsync(Guid employeeId, Guid userId, CancellationToken cancellationToken = default);
    Task UnbindAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task UnbindAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
