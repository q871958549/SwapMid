using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.EnterpriseManagement;

public interface IEmployeeRepository : IRepository<Employee, Guid>
{
    Task<bool> EmployeeNoExistsAsync(Guid enterpriseId, string employeeNo, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<bool> UserBindingExistsAsync(Guid userId, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
