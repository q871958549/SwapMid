namespace SwapMid.Scheduling;

/// <summary>定时任务单次执行的状态。</summary>
public enum JobExecutionStatus
{
    /// <summary>执行中（已开始、未结束）。</summary>
    Running = 0,

    /// <summary>成功完成。</summary>
    Succeeded = 1,

    /// <summary>失败（异常冒泡到 Hangfire）。</summary>
    Failed = 2,

    /// <summary>中断：进程重启等原因导致「执行中」记录长期未收尾，由启动时僵尸执行清理标记（非真实业务失败）。</summary>
    Interrupted = 3,

    /// <summary>跳过：因并发互斥等预期竞争未取得执行权，未执行业务，不属于成功或失败。</summary>
    Skipped = 4
}

/// <summary>定时任务的触发方式。</summary>
public enum JobTriggerKind
{
    /// <summary>定时调度（Hangfire recurring 到点触发）。</summary>
    Scheduled = 0,

    /// <summary>手动（管控页「立即执行」或 API 入队）。</summary>
    Manual = 1
}

/// <summary>带参执行的参数字段类型，决定前端渲染何种输入控件。</summary>
public enum JobParameterType
{
    /// <summary>纯文本。</summary>
    Text = 0,

    /// <summary>某天（yyyy-MM-dd）。</summary>
    Date = 1,

    /// <summary>某月（yyyy-MM）。</summary>
    Month = 2
}
