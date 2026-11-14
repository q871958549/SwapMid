using System.ComponentModel;

namespace SwapMid.DataIntegration;

public enum TriggerType
{
    [Description("手动")]
    Manual = 0,

    [Description("定时")]
    Scheduled = 1
}
