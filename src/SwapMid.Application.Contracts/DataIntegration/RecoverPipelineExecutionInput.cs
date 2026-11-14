using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Auditing;

namespace SwapMid.DataIntegration;

/// <summary>滞留运行中执行的受控恢复输入。</summary>
public sealed class RecoverPipelineExecutionInput
{
    /// <summary>恢复请求幂等标识；同一次处置重试必须复用该值。</summary>
    [Required]
    public Guid RecoveryRequestId { get; set; }

    /// <summary>调用方最后读取到的执行记录并发戳。</summary>
    [Required]
    [StringLength(40, MinimumLength = 1)]
    public string ExpectedConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>全部旧执行实例开始摘流的 UTC 时间。</summary>
    [Required]
    public DateTimeOffset DrainStartedAt { get; set; }

    /// <summary>全部具备旧执行 owner 能力的实例已退出的 UTC 确认时间。</summary>
    [Required]
    public DateTimeOffset OldOwnerExitVerifiedAt { get; set; }

    /// <summary>DBA 确认目标活跃会话与未完成事务均已消失的 UTC 时间。</summary>
    [Required]
    public DateTimeOffset TargetSessionExitVerifiedAt { get; set; }

    /// <summary>
    /// 结构化证据引用。仅允许 incident:/change:/work-order:/dba-evidence: 前缀，
    /// 后接 8–128 位 ASCII 字母数字、短横线或下划线组成的不透明编号；不得粘贴 URL、SQL、
    /// 连接串、令牌或密钥。示例：incident:INC-2026-ETL-0001。
    /// </summary>
    [Required]
    [StringLength(141, MinimumLength = 15)]
    [DisableAuditing]
    public string EvidenceReference { get; set; } = string.Empty;
}
