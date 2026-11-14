using System.ComponentModel;

namespace SwapMid.DataIntegration;

/// <summary>管理员对目标库完成核验后的最终结论。</summary>
public enum PipelineExecutionResolution
{
    [Description("确认已应用")]
    ConfirmedApplied = 0,

    [Description("确认未应用")]
    ConfirmedNotApplied = 1,
}
