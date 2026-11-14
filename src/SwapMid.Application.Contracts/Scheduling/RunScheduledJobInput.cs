namespace SwapMid.Scheduling;

/// <summary>带参 / 演练执行一次定时任务的输入。仅对已接入统一执行管道(IScheduledJobHandler)的任务有效。</summary>
public class RunScheduledJobInput
{
    /// <summary>任务 Key（与 ScheduledJobCatalog 一致）。</summary>
    public string Key { get; set; } = null!;

    /// <summary>演练模式：不写库、只发测试收件人，不触达正式订阅人。</summary>
    public bool DryRun { get; set; }

    /// <summary>演练模式的测试收件人邮箱（演练时必填）。</summary>
    public string? TestRecipient { get; set; }

    /// <summary>补偿参数（JSON），如 {"retainDays":90}；空 = 按任务默认参数执行。</summary>
    public string? ArgsJson { get; set; }

    /// <summary>
    /// 后台执行（F6）：入队立即返回（结果 Queued=true），进度与结果看执行历史——供长任务
    /// （跨库批量归档 / 全量对账同步等长耗时作业）避免顶着 HTTP 超时。
    /// 仅正式模式生效：演练必须同步等结果（操作者在弹窗前等摘要与测试邮件），传 true 且 DryRun=true 时报错。
    /// </summary>
    public bool RunInBackground { get; set; }
}
