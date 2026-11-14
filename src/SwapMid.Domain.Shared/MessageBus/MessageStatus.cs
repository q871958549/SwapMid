using System.ComponentModel;

namespace SwapMid.MessageBus;

public enum MessageStatus
{
    [Description("待投递")]
    Pending = 0,

    [Description("已投递")]
    Delivered = 1,

    [Description("失败")]
    Failed = 2,

    [Description("死信")]
    DeadLettered = 3
}
