using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.DataIntegration;

/// <summary>Apache Iceberg v2 命名引用（Branch / Tag）信息</summary>
public class CdcLakehouseRefDto
{
    public string Name { get; set; } = null!;
    public string Type { get; set; } = "tag";
    public long SnapshotId { get; set; }
    public long? Version { get; set; }
    public long? MaxRefAgeMs { get; set; }
}

/// <summary>为 Apache Iceberg v2 快照创建或更新命名标签（WAP / 审计锚点）入参</summary>
public class CreateCdcLakehouseTagInput
{
    /// <summary>标签名称（仅允许字母、数字、点、下划线与连字符，不可为 main）</summary>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string TagName { get; set; } = null!;

    /// <summary>目标快照版本号（留空则绑定当前最新快照）</summary>
    [Range(1, long.MaxValue)]
    public long? SnapshotVersion { get; set; }

    /// <summary>可选最大保留时长（毫秒，至少 60000 ms）</summary>
    [Range(60_000, long.MaxValue)]
    public long? MaxRefAgeMs { get; set; }
}

/// <summary>Apache Iceberg v2 命名标签创建结果</summary>
public class CdcLakehouseTagResultDto
{
    public Guid StreamId { get; set; }
    public string TagName { get; set; } = null!;
    public long SnapshotId { get; set; }
    public long SnapshotVersion { get; set; }
    public long MetadataVersion { get; set; }
    public string MetadataKey { get; set; } = null!;
    public long? MaxRefAgeMs { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string Message { get; set; } = null!;
}

/// <summary>Apache Iceberg v2 快照过期与孤儿文件安全清理（expireSnapshots）入参</summary>
public class ExpireCdcLakehouseSnapshotsInput
{
    /// <summary>至少保留最近 N 个快照（默认 2，含当前最新快照）</summary>
    [Range(1, 100)]
    public int RetainLast { get; set; } = 2;

    /// <summary>仅过期早于当前时间 N 分钟的快照（0 表示按 RetainLast 立即评估历史快照）</summary>
    [Range(0, 525_600)]
    public int OlderThanMinutes { get; set; } = 0;

    /// <summary>是否在非预演模式下同步物理删除已无任何保留快照引用的 Parquet/Avro 孤儿对象</summary>
    public bool DeleteOrphanedFiles { get; set; } = false;

    /// <summary>默认 true（仅输出只读清理计划，绝不修改元数据或删除对象）</summary>
    public bool DryRun { get; set; } = true;
}

/// <summary>Apache Iceberg v2 快照过期与孤儿清理计划 / 执行结果</summary>
public class CdcLakehouseExpireResultDto
{
    public Guid StreamId { get; set; }
    public bool DryRun { get; set; }
    public long CurrentMetadataVersion { get; set; }
    public long? NewMetadataVersion { get; set; }
    public int RetainedSnapshotCount { get; set; }
    public int ExpiredSnapshotCount { get; set; }
    public int PinnedByRefCount { get; set; }
    public List<long> ExpiredVersions { get; set; } = new();
    public int CandidateOrphanFileCount { get; set; }
    public int DeletedOrphanFileCount { get; set; }
    public long ReclaimableBytes { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public string Summary { get; set; } = null!;
}

/// <summary>Apache Iceberg v2 WAP 隔离分支快进发布到 main 分支入参</summary>
public class PromoteCdcLakehouseBranchInput
{
    /// <summary>待发布的源分支名称（默认 staging，不可为 main）</summary>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string BranchName { get; set; } = "staging";

    /// <summary>是否要求已配置的数据契约 SLA 处于合规状态（默认 true，违约时阻断发布）</summary>
    public bool RequireContractPass { get; set; } = true;

    /// <summary>可选：发布成功的同时为该快照原子绑定一个审计命名标签（如 release-2026-11）</summary>
    [StringLength(64)]
    public string? CreateAuditTagName { get; set; }

    /// <summary>为 true 时仅执行快进预检（不修改 main 与表元数据）</summary>
    public bool DryRun { get; set; } = false;
}

/// <summary>Apache Iceberg v2 WAP 分支快进发布结果</summary>
public class CdcLakehouseBranchPromoteResultDto
{
    public Guid StreamId { get; set; }
    public string BranchName { get; set; } = null!;
    public bool DryRun { get; set; }
    public bool AlreadyUpToDate { get; set; }
    public long? PreviousMainSnapshotId { get; set; }
    public long? PreviousMainVersion { get; set; }
    public long PromotedSnapshotId { get; set; }
    public long PromotedSnapshotVersion { get; set; }
    public long MetadataVersion { get; set; }
    public string MetadataKey { get; set; } = null!;
    public string? AuditTagName { get; set; }
    public DateTime PromotedAtUtc { get; set; }
    public string Message { get; set; } = null!;
}

/// <summary>Apache Iceberg v2 原子时间旅行回滚（Rollback main 至历史版本或命名标签/分支）入参</summary>
public class RollbackCdcLakehouseInput
{
    /// <summary>目标历史快照版本号（与 TargetTagName 二选一）</summary>
    [Range(1, long.MaxValue)]
    public long? TargetVersion { get; set; }

    /// <summary>目标命名标签或分支名（如 audit-v1 / release-280，与 TargetVersion 二选一）</summary>
    [StringLength(64)]
    public string? TargetTagName { get; set; }

    /// <summary>可选：回滚前先将当前 main 快照原子绑定为备份审计标签（保证 100% 可逆）</summary>
    [StringLength(64)]
    public string? BackupCurrentAsTagName { get; set; }

    /// <summary>默认 true（仅执行回滚预演与校验，绝不修改 main 分支或表元数据）</summary>
    public bool DryRun { get; set; } = true;
}

/// <summary>Apache Iceberg v2 原子时间旅行回滚结果</summary>
public class CdcLakehouseRollbackResultDto
{
    public Guid StreamId { get; set; }
    public bool DryRun { get; set; }
    public bool AlreadyAtTarget { get; set; }
    public long? PreviousMainSnapshotId { get; set; }
    public long? PreviousMainVersion { get; set; }
    public long TargetSnapshotId { get; set; }
    public long TargetSnapshotVersion { get; set; }
    public string? TargetRefName { get; set; }
    public string? BackupTagName { get; set; }
    public long MetadataVersion { get; set; }
    public string MetadataKey { get; set; } = null!;
    public DateTime RolledBackAtUtc { get; set; }
    public string Message { get; set; } = null!;
}
