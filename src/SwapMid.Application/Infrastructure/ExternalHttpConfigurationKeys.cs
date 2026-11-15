namespace SwapMid.Infrastructure;

/// <summary>
/// 出站 HTTP 安全边界配置键集中定义。
/// </summary>
public static class ExternalHttpConfigurationKeys
{
    public const string PrometheusUrl = "Prometheus:Url";
    public const string PrometheusAllowedPrivateNetworks = "Prometheus:AllowedPrivateNetworks";

    public const string MessageDeliveryAllowedHosts = "MessageBus:HttpDelivery:AllowedHosts";
    public const string MessageDeliveryAllowedPrivateNetworks = "MessageBus:HttpDelivery:AllowedPrivateNetworks";

    public const string BotDeliveryAllowedHosts = "MessageBus:BotDelivery:AllowedHosts";
    public const string BotDeliveryAllowedPrivateNetworks = "MessageBus:BotDelivery:AllowedPrivateNetworks";

    public const string NotificationWebhookAllowedHosts = "Notification:Webhook:AllowedHosts";
    public const string NotificationWebhookAllowedPrivateNetworks = "Notification:Webhook:AllowedPrivateNetworks";

    public const string ApiGatewayAllowedHosts = "ApiGateway:AllowedHosts";
    public const string ApiGatewayAllowedPrivateNetworks = "ApiGateway:AllowedPrivateNetworks";

    public const string DataIntegrationSinkAllowedHosts = "DataIntegration:Sinks:AllowedHosts";
    public const string DataIntegrationSinkAllowedPrivateNetworks = "DataIntegration:Sinks:AllowedPrivateNetworks";
}
