using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.MessageBus;

/// <summary>消息订阅（主题下的一个订阅者，按投递类型将消息推送到 HTTP Webhook 或 RabbitMQ）。</summary>
public class MessageSubscription : FullAuditedAggregateRoot<Guid>
{
    /// <summary>所属主题 Id。</summary>
    public Guid TopicId { get; private set; }

    /// <summary>订阅名称。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>投递类型（HTTP Webhook / RabbitMQ）。</summary>
    public DeliveryType DeliveryType { get; private set; }

    /// <summary>Webhook 回调地址（可空，仅 HTTP 投递时使用）。</summary>
    public string? EndpointUrl { get; private set; }

    /// <summary>RabbitMQ 交换机名称（可空，仅 RabbitMQ 投递时使用）。</summary>
    public string? ExchangeName { get; private set; }

    /// <summary>RabbitMQ 路由键（可空，仅 RabbitMQ 投递时使用）。</summary>
    public string? RoutingKey { get; private set; }

    /// <summary>过滤规则（可空，按消息内容筛选是否投递给本订阅）。</summary>
    public string? FilterRule { get; private set; }

    /// <summary>HMAC 签名密钥（可空，仅 HTTP 投递用）。配了则投递带 <c>X-SwapMid-Signature: sha256=HMAC(secret, body)</c> 供消费者验真。</summary>
    public string? Secret { get; private set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; private set; }

    private MessageSubscription() { }

    public MessageSubscription(Guid id, Guid topicId, string name, DeliveryType deliveryType,
        string? endpointUrl, string? exchangeName, string? routingKey, string? filterRule = null, string? secret = null)
        : base(id)
    {
        TopicId = topicId;
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MessageSubscriptionConsts.MaxNameLength);
        DeliveryType = deliveryType;
        SetTarget(deliveryType, endpointUrl, exchangeName, routingKey);
        FilterRule = filterRule;
        Secret = secret;
        IsEnabled = true;
    }

    public void Update(string name, DeliveryType deliveryType, string? endpointUrl,
        string? exchangeName, string? routingKey, string? filterRule, string? secret)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MessageSubscriptionConsts.MaxNameLength);
        DeliveryType = deliveryType;
        SetTarget(deliveryType, endpointUrl, exchangeName, routingKey);
        FilterRule = filterRule;
        Secret = secret;
    }

    // 按投递类型校验目标:RabbitMq 必须有 ExchangeName;其余(HTTP / BotWebhook 群机器人)必须有 EndpointUrl;互斥清空另一侧。
    private void SetTarget(DeliveryType deliveryType, string? endpointUrl, string? exchangeName, string? routingKey)
    {
        if (deliveryType == DeliveryType.RabbitMq)
        {
            ExchangeName = Check.NotNullOrWhiteSpace(exchangeName, nameof(exchangeName), MessageSubscriptionConsts.MaxExchangeNameLength);
            RoutingKey = routingKey;
            EndpointUrl = null;
        }
        else
        {
            EndpointUrl = Check.NotNullOrWhiteSpace(endpointUrl, nameof(endpointUrl), MessageSubscriptionConsts.MaxEndpointUrlLength);
            ExchangeName = null;
            RoutingKey = null;
        }
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
}
