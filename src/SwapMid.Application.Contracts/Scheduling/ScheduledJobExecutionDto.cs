using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.Scheduling;

/// <summary>定时任务执行历史（一次执行一条）。</summary>
public class ScheduledJobExecutionDto : EntityDto<Guid>
{
    /// <summary>受管控任务 key（ScheduledJobCatalog.Key）；手动入队作业可空。</summary>
    public string? JobKey { get; set; }

    /// <summary>Hangfire 作业类型名。</summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>Hangfire 作业方法名。</summary>
    public string? JobMethod { get; set; }

    /// <summary>触发方式（定时 / 手动）。</summary>
    public JobTriggerKind TriggerKind { get; set; }

    /// <summary>执行状态（执行中 / 成功 / 失败 / 中断 / 跳过）。</summary>
    public JobExecutionStatus Status { get; set; }

    /// <summary>开始时间。</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>结束时间（执行中为空）。</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>耗时（毫秒）。</summary>
    public long? DurationMs { get; set; }

    /// <summary>业务摘要（如「生成 1985 行」）。</summary>
    public string? Summary { get; set; }

    /// <summary>失败错误信息。</summary>
    public string? Error { get; set; }

    /// <summary>是否演练；老轨 Hangfire 过滤器记录的执行恒为 false。</summary>
    public bool IsDryRun { get; set; }

    /// <summary>带参执行的参数 JSON；无参或老轨执行为空。</summary>
    public string? ArgsJson { get; set; }

    /// <summary>操作人（手动触发时的当前用户）；定时触发 / 老轨记录为空。</summary>
    public Guid? CreatorId { get; set; }
}

/// <summary>执行历史查询入参。</summary>
public class GetScheduledJobExecutionListInput : PagedAndSortedResultRequestDto
{
    /// <summary>按任务 key 过滤（ScheduledJobCatalog.Key）；空=全部。</summary>
    public string? JobKey { get; set; }

    /// <summary>按状态过滤；空=全部。</summary>
    public JobExecutionStatus? Status { get; set; }

    /// <summary>按触发方式过滤；空=全部。</summary>
    public JobTriggerKind? TriggerKind { get; set; }

    /// <summary>按是否演练过滤；空=全部（含演练与正式）。</summary>
    public bool? IsDryRun { get; set; }

    /// <summary>开始时间下限（含）；按 StartedAt 过滤。</summary>
    public DateTime? StartedFrom { get; set; }

    /// <summary>开始时间上限（含）；按 StartedAt 过滤。</summary>
    public DateTime? StartedTo { get; set; }
}
