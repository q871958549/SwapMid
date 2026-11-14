using System.ComponentModel;

namespace SwapMid.MessageBus;

public enum DeliveryType
{
    [Description("HTTP Webhook")]
    Http = 0,

    [Description("RabbitMQ")]
    RabbitMq = 1,

    [Description("Bot Webhook")]
    BotWebhook = 2
}
