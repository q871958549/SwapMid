using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.Scheduling;

/// <summary>
/// 定时任务执行历史：每次 Hangfire 作业执行（定时或手动）留一条，供「系统管理→定时任务」页查历史、排查失败。
/// 由 Host 层 <c>ScheduledJobExecutionFilter</c>（Hangfire IServerFilter）在作业前后写入；后台无当前用户，故用
/// <see cref="CreationAuditedEntity{TKey}"/>（CreatorId 可空）。参考 <c>NotificationLog</c> 的台账写法。
/// </summary>
public class ScheduledJobExecution : CreationAuditedEntity<Guid>
{
    /// <summary>受管控任务 key（<c>ScheduledJobCatalog.Key</c>）；定时触发时=Hangfire recurring id，手动入队作业可空。</summary>
    public string? JobKey { get; private set; }

    /// <summary>Hangfire 作业类型名（如 ExcessMaterialReportJob）。</summary>
    public string JobType { get; private set; } = string.Empty;

    /// <summary>Hangfire 作业方法名。</summary>
    public string? JobMethod { get; private set; }

    /// <summary>Hangfire 作业 id。</summary>
    public string? HangfireJobId { get; private set; }

    public JobTriggerKind TriggerKind { get; private set; }

    public JobExecutionStatus Status { get; private set; }

    public DateTime StartedAt { get; private set; }

    public DateTime? FinishedAt { get; private set; }

    /// <summary>耗时（毫秒）；未结束时为 null。</summary>
    public long? DurationMs { get; private set; }

    /// <summary>业务摘要（如「生成 1985 行」）；任务迁到新契约（P1b）后由任务返回填充。</summary>
    public string? Summary { get; private set; }

    /// <summary>失败时的错误信息（截断）。</summary>
    public string? Error { get; private set; }

    /// <summary>是否演练（不写库、只发测试收件人）；老轨 Hangfire 过滤器记录的执行恒为 false（无演练概念）。</summary>
    public bool IsDryRun { get; private set; }

    /// <summary>带参执行的参数 JSON（如 {"month":"2026-06"}）；无参或老轨执行为空。</summary>
    public string? ArgsJson { get; private set; }

    private ScheduledJobExecution()
    {
    }

    public ScheduledJobExecution(
        Guid id,
        string? jobKey,
        string jobType,
        string? jobMethod,
        string? hangfireJobId,
        JobTriggerKind triggerKind,
        DateTime startedAt,
        bool isDryRun = false,
        string? argsJson = null)
        : base(id)
    {
        JobKey = jobKey;
        JobType = jobType;
        JobMethod = jobMethod;
        HangfireJobId = hangfireJobId;
        TriggerKind = triggerKind;
        Status = JobExecutionStatus.Running;
        StartedAt = startedAt;
        IsDryRun = isDryRun;
        ArgsJson = Truncate(argsJson, 1024);
    }

    /// <summary>标记完成（成功或失败），记录结束时间、耗时、摘要与错误。</summary>
    public void MarkFinished(DateTime finishedAt, bool success, string? summary, string? error)
    {
        FinishedAt = finishedAt;
        DurationMs = (long)(finishedAt - StartedAt).TotalMilliseconds;
        Status = success ? JobExecutionStatus.Succeeded : JobExecutionStatus.Failed;
        Summary = Truncate(summary, 512);
        Error = Truncate(error, 2048);
    }

    /// <summary>标记中断：Host 启动时对超阈值仍处「执行中」的历史兜底收尾（进程重启等原因导致未正常结束，非真实业务失败）。</summary>
    public void MarkInterrupted(DateTime finishedAt, string? summary = null)
    {
        FinishedAt = finishedAt;
        DurationMs = (long)(finishedAt - StartedAt).TotalMilliseconds;
        Status = JobExecutionStatus.Interrupted;
        Summary = Truncate(summary ?? "进程重启或超时未正常结束，已标记为中断", 512);
    }

    /// <summary>标记跳过：因并发互斥等预期竞争未取得执行权，未执行业务，非成功亦非失败。</summary>
    public void MarkSkipped(DateTime finishedAt, string? summary = null)
    {
        FinishedAt = finishedAt;
        DurationMs = (long)(finishedAt - StartedAt).TotalMilliseconds;
        Status = JobExecutionStatus.Skipped;
        Summary = Truncate(summary ?? "未取得任务执行权，本次跳过", 512);
        Error = null;
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
