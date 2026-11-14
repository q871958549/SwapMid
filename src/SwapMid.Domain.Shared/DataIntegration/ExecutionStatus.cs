using System.ComponentModel;

namespace SwapMid.DataIntegration;

public enum ExecutionStatus
{
    [Description("待执行")]
    Pending = 0,

    [Description("执行中")]
    Running = 1,

    [Description("成功")]
    Succeeded = 2,

    [Description("失败")]
    Failed = 3,

    [Description("已取消")]
    Cancelled = 4,

    /// <summary>
    /// 目标事务已进入提交边界但客户端无法确认数据库最终结果；或在完整摘流、旧 owner 退出且
    /// 目标活跃会话/事务消失后，将遗留 Running 受控恢复为仍无法判定外部结果的状态。
    /// 两种来源都必须人工对账，禁止自动重试。
    /// </summary>
    [Description("结果待核对")]
    OutcomeUnknown = 5
}
