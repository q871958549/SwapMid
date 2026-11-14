using System.ComponentModel;

namespace SwapMid.MessageBus;

public enum TopicType
{
    [Description("队列")]
    Queue = 0,

    [Description("广播")]
    Broadcast = 1
}
