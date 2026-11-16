using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace SwapMid.DataIntegration;

public class CdcStreamDto : FullAuditedEntityDto<Guid>
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public Guid SourceId { get; set; }
    public string? SourceName { get; set; }
    public string PublicationName { get; set; } = null!;
    public string SlotName { get; set; } = null!;
    public string? MonitoredTables { get; set; }
    public bool AutoCreatePublicationAndSlot { get; set; }
    public int PollIntervalSeconds { get; set; }
    public int MaxBatchSize { get; set; }
    public CdcTargetMode TargetMode { get; set; }
    public string? TargetTopicName { get; set; }
    public Guid? TargetPipelineId { get; set; }
    public string? TargetPipelineName { get; set; }
    public Guid? TargetSinkId { get; set; }
    public string? TargetSinkName { get; set; }
    public DataSourceType? TargetSinkType { get; set; }
    public string? TargetSinkDestination { get; set; }
    public string? TargetSinkKeyColumns { get; set; }
    public bool InitialSnapshotEnabled { get; set; }
    public CdcSnapshotStatus SnapshotStatus { get; set; }
    public DateTime? SnapshotCompletedAt { get; set; }
    public long SnapshotRowCount { get; set; }
    public CdcStreamStatus Status { get; set; }
    public string? CheckpointLsn { get; set; }
    public DateTime? LastCapturedAt { get; set; }
    public long LastCapturedCount { get; set; }
    public long TotalCapturedCount { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
}

public class CdcStreamCreateUpdateDto
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = null!;

    [StringLength(512)]
    public string? Description { get; set; }

    [Required]
    public Guid SourceId { get; set; }

    [Required]
    [StringLength(63)]
    public string PublicationName { get; set; } = null!;

    [Required]
    [StringLength(63)]
    public string SlotName { get; set; } = null!;

    [StringLength(2048)]
    public string? MonitoredTables { get; set; }

    public bool AutoCreatePublicationAndSlot { get; set; } = true;

    [Range(5, 3600)]
    public int PollIntervalSeconds { get; set; } = 15;

    [Range(1, 5000)]
    public int MaxBatchSize { get; set; } = 500;

    public CdcTargetMode TargetMode { get; set; } = CdcTargetMode.MessageBusTopic;

    [StringLength(128)]
    public string? TargetTopicName { get; set; }

    public Guid? TargetPipelineId { get; set; }

    public Guid? TargetSinkId { get; set; }

    [StringLength(256)]
    public string? TargetSinkDestination { get; set; }

    [StringLength(256)]
    public string? TargetSinkKeyColumns { get; set; }

    public bool InitialSnapshotEnabled { get; set; }
}

public class GetCdcStreamListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
    public Guid? SourceId { get; set; }
    public CdcStreamStatus? Status { get; set; }
}

public class CdcTableReplicaIdentityDto
{
    public string Schema { get; set; } = null!;
    public string Table { get; set; } = null!;
    public string ReplicaIdentity { get; set; } = null!;
}

public class CdcPreflightResultDto
{
    public bool IsReady { get; set; }
    public string? WalLevel { get; set; }
    public string? PostgreSqlVersion { get; set; }
    public bool PublicationExists { get; set; }
    public bool SlotExists { get; set; }
    public bool SlotActive { get; set; }
    public string? SlotPlugin { get; set; }
    public string? ConfirmedFlushLsn { get; set; }
    public string? CurrentWalLsn { get; set; }
    public long? WalLagBytes { get; set; }
    public List<CdcTableReplicaIdentityDto> TableReplicaIdentities { get; set; } = new();
    public List<string> Diagnostics { get; set; } = new();
    public string Summary { get; set; } = null!;
}

public class CdcDropSlotResultDto
{
    public Guid StreamId { get; set; }
    public string SlotName { get; set; } = null!;
    public bool Dropped { get; set; }
    public string Message { get; set; } = null!;
}

public class CdcCaptureResultDto
{
    public Guid StreamId { get; set; }
    public string? PreviousCheckpointLsn { get; set; }
    public string? NewCheckpointLsn { get; set; }
    public long CapturedEventCount { get; set; }
    public long DeliveredCount { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public string Message { get; set; } = null!;
}

public class CdcResetCheckpointInput
{
    [StringLength(32)]
    public string? CheckpointLsn { get; set; }
}

public class CdcSinkColumnTemplateDto
{
    public string Name { get; set; } = null!;
    public string LogicalType { get; set; } = null!;
    public string ClickHouseType { get; set; } = null!;
    public bool Nullable { get; set; }
    public string Description { get; set; } = null!;
}

public class CdcSinkTemplateDto
{
    public Guid StreamId { get; set; }
    public DataSourceType TargetType { get; set; }
    public string Destination { get; set; } = null!;
    public bool? TargetExists { get; set; }
    public string? PartitionKeyExpression { get; set; }
    public List<CdcSinkColumnTemplateDto> Columns { get; set; } = new();
    public string? ClickHouseDdl { get; set; }
    public string SamplePayloadJson { get; set; } = null!;
    public string Summary { get; set; } = null!;
}

public class CdcSnapshotResultDto
{
    public Guid StreamId { get; set; }
    public string? ConsistentPointLsn { get; set; }
    public long SnapshotRowCount { get; set; }
    public long DeliveredCount { get; set; }
    public bool UsedExportedSnapshot { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public string Message { get; set; } = null!;
}
