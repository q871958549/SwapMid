namespace SwapMid.Scheduling;

/// <summary>带参 / 演练执行的结果。</summary>
public class RunScheduledJobResultDto
{
    /// <summary>是否成功（Queued=true 时表示"已成功入队"，执行成败看执行历史）。</summary>
    public bool Success { get; set; }

    /// <summary>同步执行终态；后台入队尚未产生结果时为空。Skipped 保持 Success=true 兼容旧客户端。</summary>
    public JobExecutionStatus? Status { get; set; }

    /// <summary>业务摘要（如「演练 3 行」「生成 12 行」；后台入队时为入队提示文案）。</summary>
    public string? Summary { get; set; }

    /// <summary>失败时的错误信息。</summary>
    public string? Error { get; set; }

    /// <summary>已入队后台执行（RunInBackground=true 的返回形态）：结果尚未产生，进度与成败看执行历史。</summary>
    public bool Queued { get; set; }
}
