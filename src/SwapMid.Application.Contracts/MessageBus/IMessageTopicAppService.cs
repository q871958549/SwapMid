using System;
using Volo.Abp.Application.Services;

namespace SwapMid.MessageBus;

public interface IMessageTopicAppService
    : ICrudAppService<MessageTopicDto, Guid, GetMessageTopicListInput, MessageTopicCreateUpdateDto>
{
}
