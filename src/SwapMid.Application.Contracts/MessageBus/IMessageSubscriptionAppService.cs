using System;
using Volo.Abp.Application.Services;

namespace SwapMid.MessageBus;

public interface IMessageSubscriptionAppService
    : ICrudAppService<MessageSubscriptionDto, Guid, GetMessageSubscriptionListInput, MessageSubscriptionCreateUpdateDto>
{
}
