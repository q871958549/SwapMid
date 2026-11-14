using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.DataIntegration;

/// <summary>数据管道（定义从源数据源抽取、转换到目标数据源的 ETL 流程及其调度）。</summary>
public class DataPipeline : FullAuditedAggregateRoot<Guid>
{
    /// <summary>管道名称。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>管道描述（可空）。</summary>
    public string? Description { get; private set; }

    /// <summary>源数据源 Id（数据抽取来源）。</summary>
    public Guid SourceId { get; private set; }

    /// <summary>目标数据源 Id（可空，数据写入目标）。</summary>
    public Guid? TargetId { get; private set; }

    /// <summary>转换配置（JSON，可空，定义字段映射 / 清洗 / 转换规则）。</summary>
    [DisableAuditing]
    public string? TransformConfig { get; private set; }

    /// <summary>调度 Cron 表达式（可空，定时执行时使用）。</summary>
    public string? ScheduleCron { get; private set; }

    /// <summary>是否启用定时调度。</summary>
    public bool ScheduleEnabled { get; private set; }

    /// <summary>管道状态（草稿 / 启用 / 暂停 / 归档）。</summary>
    public PipelineStatus Status { get; private set; }

    /// <summary>最近一次执行时间（可空）。</summary>
    public DateTime? LastRunAt { get; private set; }

    /// <summary>最近一次执行结果状态（可空）。</summary>
    public ExecutionStatus? LastRunStatus { get; private set; }

    private DataPipeline() { }

    public DataPipeline(
        Guid id,
        string name,
        Guid sourceId,
        Guid? targetId = null,
        string? description = null,
        string? transformConfig = null,
        string? scheduleCron = null)
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), DataPipelineConsts.MaxNameLength);
        SourceId = sourceId;
        TargetId = targetId;
        Description = description;
        TransformConfig = transformConfig;
        ScheduleCron = scheduleCron;
        ScheduleEnabled = false;
        Status = PipelineStatus.Draft;
    }

    public void Update(
        string name,
        Guid sourceId,
        Guid? targetId,
        string? description,
        string? transformConfig,
        string? scheduleCron,
        bool scheduleEnabled)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), DataPipelineConsts.MaxNameLength);
        SourceId = sourceId;
        TargetId = targetId;
        Description = description;
        TransformConfig = transformConfig;
        ScheduleCron = scheduleCron;
        ScheduleEnabled = scheduleEnabled;
    }

    public void Activate() => Status = PipelineStatus.Active;
    public void Pause() => Status = PipelineStatus.Paused;
    public void Archive() => Status = PipelineStatus.Archived;

    public void RecordExecution(DateTime startedAt, ExecutionStatus status)
    {
        if (LastRunAt.HasValue && LastRunAt.Value > startedAt)
        {
            return;
        }

        LastRunAt = startedAt;
        LastRunStatus = status;
    }
}
