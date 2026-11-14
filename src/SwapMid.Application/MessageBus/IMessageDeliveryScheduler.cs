using System;
using System.Threading.Tasks;

namespace SwapMid.MessageBus;

public interface IMessageDeliveryScheduler
{
    Task EnqueueAsync(Guid messageRecordId, TimeSpan? delay = null);
}
