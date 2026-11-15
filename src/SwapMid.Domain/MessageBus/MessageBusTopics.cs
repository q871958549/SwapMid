using SwapMid.SharedKernel.IntegrationEvents;

namespace SwapMid.MessageBus;

/// <summary>
/// 平台内置消息主题编码常量。
/// </summary>
public static class MessageBusTopics
{
    public const string IntegrationPipelineEvents = IntegrationTopicNames.PipelineEvents;
    public const string IntegrationCdcEvents = IntegrationTopicNames.CdcEvents;
    public const string SystemAlerts = "system.alerts";
}
