using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.MessageBus;

/// <summary>消息订阅</summary>
public class MessageSubscriptionDto : FullAuditedEntityDto<Guid>
{
    /// <summary>所属主题 ID</summary>
    public Guid TopicId { get; set; }
    /// <summary>所属主题名称</summary>
    public string? TopicName { get; set; }
    /// <summary>订阅名称</summary>
    public string Name { get; set; } = null!;
    /// <summary>投递方式(HTTP/RabbitMq)</summary>
    public DeliveryType DeliveryType { get; set; }
    /// <summary>端点 URL(HTTP 投递目标)</summary>
    public string? EndpointUrl { get; set; }
    /// <summary>交换机名称(RabbitMq)</summary>
    public string? ExchangeName { get; set; }
    /// <summary>路由键(RabbitMq)</summary>
    public string? RoutingKey { get; set; }
    /// <summary>过滤规则</summary>
    public string? FilterRule { get; set; }
    /// <summary>是否已配置 HMAC 签名密钥(密钥本身写不可读,不回传)</summary>
    public bool HasSecret { get; set; }
    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }
}
