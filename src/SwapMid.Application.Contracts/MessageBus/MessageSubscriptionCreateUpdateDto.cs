using System;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.MessageBus;

/// <summary>消息订阅新增/编辑入参</summary>
public class MessageSubscriptionCreateUpdateDto
{
    /// <summary>所属主题 ID</summary>
    [Required] public Guid TopicId { get; set; }
    /// <summary>订阅名称</summary>
    [Required][MaxLength(128)] public string Name { get; set; } = null!;
    /// <summary>投递方式(HTTP/RabbitMq)</summary>
    public DeliveryType DeliveryType { get; set; } = DeliveryType.Http;
    /// <summary>端点 URL(HTTP 投递目标)</summary>
    [MaxLength(512)] public string? EndpointUrl { get; set; }       // HTTP 订阅必填(Domain 层按类型校验)
    /// <summary>交换机名称(RabbitMq)</summary>
    [MaxLength(256)] public string? ExchangeName { get; set; }      // RabbitMq 订阅必填
    /// <summary>路由键(RabbitMq)</summary>
    [MaxLength(256)] public string? RoutingKey { get; set; }        // RabbitMq 可选(默认用 Subject)
    /// <summary>过滤规则</summary>
    [MaxLength(1024)] public string? FilterRule { get; set; }
    /// <summary>HMAC 签名密钥(可空,仅 HTTP 投递用;配了则投递带 X-SwapMid-Signature 头供消费者验真)</summary>
    [MaxLength(256)] public string? Secret { get; set; }
    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}
