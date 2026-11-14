using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.SharedKernel.Employees;

/// <summary>员工入职建档契约。仅招聘域消费，实现在企业管理域内。</summary>
public interface IEmployeeRegistrar
{
    Task<Guid> RegisterAsync(EmployeeRegistration registration, CancellationToken cancellationToken = default);
}

/// <summary>员工入职建档所需的最小数据。</summary>
public sealed record EmployeeRegistration(
    Guid EnterpriseId,
    string EmployeeNo,
    string Name,
    Guid? DepartmentId = null,
    string? Email = null,
    string? Phone = null,
    string? PositionText = null,
    Guid? PositionId = null);
