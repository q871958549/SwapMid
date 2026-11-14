using System.ComponentModel;

namespace SwapMid.DataIntegration;

public enum PipelineStatus
{
    [Description("草稿")]
    Draft = 0,

    [Description("启用")]
    Active = 1,

    [Description("暂停")]
    Paused = 2,

    [Description("归档")]
    Archived = 3
}
