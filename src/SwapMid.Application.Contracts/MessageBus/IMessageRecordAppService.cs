using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwapMid.SharedKernel;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SwapMid.MessageBus;

public interface IMessageRecordAppService : IApplicationService
{
    Task<PagedResultDto<MessageRecordDto>> GetListAsync(GetMessageRecordListInput input);
    Task<MessageRecordDto> GetAsync(Guid id);
    /// <summary>该记录对各订阅的逐条投递明细(修缺陷A 后的多订阅视图)。</summary>
    Task<List<MessageDeliveryAttemptDto>> GetAttemptsAsync(Guid id);
    Task<MessageRecordDto> PublishAsync(PublishMessageDto input);
    Task ReplayAsync(Guid id);
    Task<BatchOperationResultDto> BatchReplayAsync(BatchIdsInput input);
}
