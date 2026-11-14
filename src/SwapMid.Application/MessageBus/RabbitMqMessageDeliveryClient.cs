using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using Volo.Abp.DependencyInjection;
using Volo.Abp.RabbitMQ;

namespace SwapMid.MessageBus;

// RabbitMq 投递通道:用 ABP IChannelPool(信道池)直发到订阅动态指定的 exchange。
// 注意:这里走底层 IChannelPool 而非 IDistributedEventBus —— 因为目标 exchange/routingKey
// 是用户运行时配置的任意值,而 DistributedEventBus 按事件类型固定 exchange,发不到动态目标。
public class RabbitMqMessageDeliveryClient : IMessageDeliveryClient, ITransientDependency
{
    private readonly IChannelPool _channelPool;

    public RabbitMqMessageDeliveryClient(IChannelPool channelPool)
    {
        _channelPool = channelPool;
    }

    public DeliveryType DeliveryType => DeliveryType.RabbitMq;

    public async Task<MessageDeliveryResponse> DeliverAsync(MessageDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var accessor = await _channelPool.AcquireAsync();
            var channel = accessor.Channel;

            // 幂等声明(已存在则无操作);首次声明后 type/durable 不可变更
            await channel.ExchangeDeclareAsync(
                exchange: request.ExchangeName!,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                topicName = request.TopicName,
                subject = request.Subject,
                payload = request.Payload,
                subscriptionName = request.SubscriptionName
            }));

            var properties = new BasicProperties
            {
                Persistent = true,                 // 消息持久化(配合 durable exchange/queue 不丢)
                ContentType = "application/json"
            };

            await channel.BasicPublishAsync(
                exchange: request.ExchangeName!,
                routingKey: string.IsNullOrWhiteSpace(request.RoutingKey) ? request.Subject : request.RoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            return MessageDeliveryResponse.Success();
        }
        catch (Exception ex)
        {
            return MessageDeliveryResponse.Failure(ex.Message);
        }
    }
}
