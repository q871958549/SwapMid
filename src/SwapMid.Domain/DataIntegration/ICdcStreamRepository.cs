using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.DataIntegration;

public interface ICdcStreamRepository : IRepository<CdcStreamDefinition, Guid>
{
    Task<bool> NameExistsAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);

    Task<long> GetCountAsync(
        string? filter = null,
        Guid? sourceId = null,
        CdcStreamStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<List<CdcStreamDefinition>> GetListAsync(
        string? filter = null,
        Guid? sourceId = null,
        CdcStreamStatus? status = null,
        string? sorting = null,
        int maxResultCount = int.MaxValue,
        int skipCount = 0,
        CancellationToken cancellationToken = default);

    Task<List<CdcStreamDefinition>> GetActiveAsync(
        CancellationToken cancellationToken = default);
}
