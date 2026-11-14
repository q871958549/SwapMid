using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Auditing;

namespace SwapMid.DataIntegration;

/// <summary>数据管道新增/编辑入参</summary>
public class DataPipelineCreateUpdateDto
{
    /// <summary>管道名称</summary>
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = null!;

    /// <summary>管道描述</summary>
    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>源数据源 ID</summary>
    [Required]
    public Guid SourceId { get; set; }

    /// <summary>目标数据源 ID</summary>
    public Guid? TargetId { get; set; }

    /// <summary>数据转换配置(JSON)</summary>
    [DisableAuditing]
    [MaxLength(4000)]
    public string? TransformConfig { get; set; }

    /// <summary>调度 Cron 表达式</summary>
    [MaxLength(128)]
    public string? ScheduleCron { get; set; }

    /// <summary>是否启用定时调度</summary>
    public bool ScheduleEnabled { get; set; }
}
