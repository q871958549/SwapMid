using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.EnterpriseManagement;

public interface IEnterpriseRepository : IRepository<Enterprise, Guid>
{
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
