using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.DataIntegration;

/// <summary>数据管道</summary>
public class DataPipelineDto : FullAuditedEntityDto<Guid>
{
    /// <summary>管道名称</summary>
    public string Name { get; set; } = null!;
    /// <summary>管道描述</summary>
    public string? Description { get; set; }
    /// <summary>源数据源 ID</summary>
    public Guid SourceId { get; set; }
    /// <summary>源数据源名称</summary>
    public string? SourceName { get; set; }
    /// <summary>目标数据源 ID</summary>
    public Guid? TargetId { get; set; }
    /// <summary>目标数据源名称</summary>
    public string? TargetName { get; set; }
    /// <summary>数据转换配置(JSON)</summary>
    public string? TransformConfig { get; set; }
    /// <summary>调度 Cron 表达式</summary>
    public string? ScheduleCron { get; set; }
    /// <summary>是否启用定时调度</summary>
    public bool ScheduleEnabled { get; set; }
    /// <summary>管道状态</summary>
    public PipelineStatus Status { get; set; }
    /// <summary>最近一次运行时间</summary>
    public DateTime? LastRunAt { get; set; }
    /// <summary>最近一次运行状态</summary>
    public ExecutionStatus? LastRunStatus { get; set; }
}
