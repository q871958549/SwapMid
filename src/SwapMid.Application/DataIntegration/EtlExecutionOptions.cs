namespace SwapMid.DataIntegration;

/// <summary>
/// ETL 跨实例总资源预算。默认只允许两个不同目标并发执行；0 与超过硬上限的配置在 Host
/// 启动阶段失败，不能通过部署参数把保护静默关闭或无限放大。
/// </summary>
public sealed class EtlExecutionOptions
{
    public const string SectionName = "DataIntegration:EtlExecution";
    public const int DefaultMaxConcurrentExecutions = 2;
    public const int HardMaxConcurrentExecutions = 16;
    public const int DefaultNotificationDeliveryTimeoutSeconds = 30;
    public const int MinNotificationDeliveryTimeoutSeconds = 1;
    public const int HardMaxNotificationDeliveryTimeoutSeconds = 120;

    public int MaxConcurrentExecutions { get; set; } =
        DefaultMaxConcurrentExecutions;

    public int NotificationDeliveryTimeoutSeconds { get; set; } =
        DefaultNotificationDeliveryTimeoutSeconds;

    public static bool IsValid(EtlExecutionOptions options)
        => options.MaxConcurrentExecutions is >= 1 and <=
            HardMaxConcurrentExecutions &&
           options.NotificationDeliveryTimeoutSeconds is >=
               MinNotificationDeliveryTimeoutSeconds and <=
               HardMaxNotificationDeliveryTimeoutSeconds;

    public const string ValidationMessage =
        "DataIntegration:EtlExecution:MaxConcurrentExecutions must be between 1 and 16; " +
        "NotificationDeliveryTimeoutSeconds must be between 1 and 120.";
}
