using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.MessageBus;

public interface IMessageRecordRepository : IRepository<MessageRecord, Guid>
{
    Task<List<MessageRecord>> GetListAsync(
        Guid? topicId = null,
        MessageStatus? status = null,
        string? filter = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        string? sorting = null,
        int maxResultCount = 20,
        int skipCount = 0,
        CancellationToken cancellationToken = default);

    Task<int> GetCountAsync(
        Guid? topicId = null,
        MessageStatus? status = null,
        string? filter = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        CancellationToken cancellationToken = default);
}
