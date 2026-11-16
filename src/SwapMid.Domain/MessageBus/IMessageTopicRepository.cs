using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.MessageBus;

public interface IMessageTopicRepository : IRepository<MessageTopic, Guid>
{
    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task<MessageTopic?> FindNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MessageTopic?> FindByNameNoTrackingAsync(string name, CancellationToken cancellationToken = default);

    Task<HashSet<Guid>> GetExistingIdsNoTrackingAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);
}
