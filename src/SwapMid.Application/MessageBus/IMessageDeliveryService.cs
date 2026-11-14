using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.MessageBus;

public interface IMessageDeliveryService
{
    Task DeliverAsync(MessageRecord record, MessageTopic topic, CancellationToken cancellationToken = default);
}
