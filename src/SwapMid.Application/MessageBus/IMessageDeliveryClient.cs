using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.MessageBus;

public interface IMessageDeliveryClient
{
    // 本实现负责哪种投递协议;MessageDeliveryService 按订阅的 DeliveryType 路由到对应实现
    DeliveryType DeliveryType { get; }

    Task<MessageDeliveryResponse> DeliverAsync(MessageDeliveryRequest request, CancellationToken cancellationToken = default);
}

public class MessageDeliveryRequest
{
    public Guid MessageId { get; init; }          // 消息记录 Id(HTTP 投递作幂等头 X-SwapMid-Message-Id,消费者去重)
    public string? Secret { get; init; }          // HMAC 签名密钥(可空,仅 HTTP;配了则加 X-SwapMid-Signature 头)
    public string? EndpointUrl { get; init; }     // HTTP 投递目标
    public string? ExchangeName { get; init; }    // RabbitMq 目标 exchange
    public string? RoutingKey { get; init; }      // RabbitMq routing key(空则用 Subject)
    public string TopicName { get; init; } = null!;
    public string Subject { get; init; } = null!;
    public string Payload { get; init; } = null!;
    public string SubscriptionName { get; init; } = null!;
    public DateTime? OccurredAtUtc { get; init; }
}

public class MessageDeliveryResponse
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }

    public static MessageDeliveryResponse Success() => new() { Succeeded = true };

    public static MessageDeliveryResponse Failure(string? errorMessage) => new()
    {
        Succeeded = false,
        ErrorMessage = errorMessage
    };
}
