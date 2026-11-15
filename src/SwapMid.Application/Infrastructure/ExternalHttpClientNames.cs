namespace SwapMid.Infrastructure;

/// <summary>
/// 外部 HTTP 客户端统一命名，配合 IHttpClientFactory 与 Polly 弹性管道使用。
/// </summary>
public static class ExternalHttpClientNames
{
    public const string Prometheus = "Prometheus";
    public const string MessageDeliveryHttp = "MessageDeliveryHttp";
    public const string MessageDeliveryBotWebhook = "MessageDeliveryBotWebhook";
    public const string NotificationWebhook = "NotificationWebhook";
    public const string ApiGateway = "ApiGateway";
}
