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
    public CdcSourceType SourceType { get; set; }
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
    public CdcDeletePolicy TargetDeletePolicy { get; set; }
    public List<CdcFieldMappingDto> TargetFieldMappings { get; set; } = new();
    public string? SoftDeleteColumn { get; set; }
    public string? SoftDeleteTimestampColumn { get; set; }
    public int? LakehouseCompactMinFiles { get; set; }
    public LakehouseTableFormat LakehouseTableFormat { get; set; }
    public LakehousePartitionStrategy LakehousePartitionStrategy { get; set; }
    public LakehouseCatalogType CatalogType { get; set; }
    public Guid? CatalogSinkId { get; set; }
    public string? CatalogSinkName { get; set; }
    public string? CatalogNamespace { get; set; }
    public string? CatalogTableName { get; set; }
    public long? CatalogLastSyncedVersion { get; set; }
    public DateTime? CatalogLastSyncedAt { get; set; }
    public string? CatalogLastSyncStatus { get; set; }
    public CdcSchemaDriftPolicy SchemaDriftPolicy { get; set; }
    public string? SchemaBaselineHash { get; set; }
    public bool SchemaDriftDetected { get; set; }
    public string? SchemaDriftSummary { get; set; }
    public DateTime? SchemaDriftDetectedAt { get; set; }
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
    public int? MaxFreshnessLagSeconds { get; set; }
    public long? MaxAllowedWalLagBytes { get; set; }
    public string? RequiredColumns { get; set; }
    public bool ContractViolated { get; set; }
    public string? ContractViolationSummary { get; set; }
    public DateTime? ContractLastEvaluatedAt { get; set; }
    public long? FreshnessLagSeconds { get; set; }
    public CdcContractStatus ContractStatus { get; set; }
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

    public CdcSourceType SourceType { get; set; } = CdcSourceType.PostgreSqlLogicalReplication;

    [StringLength(63)]
    public string? PublicationName { get; set; }

    [StringLength(63)]
    public string? SlotName { get; set; }

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

    public CdcDeletePolicy TargetDeletePolicy { get; set; } = CdcDeletePolicy.DeleteRow;

    public List<CdcFieldMappingDto>? TargetFieldMappings { get; set; }

    [StringLength(128)]
    public string? SoftDeleteColumn { get; set; }

    [StringLength(128)]
    public string? SoftDeleteTimestampColumn { get; set; }

    [Range(2, 100)]
    public int? LakehouseCompactMinFiles { get; set; }

    public LakehouseTableFormat LakehouseTableFormat { get; set; } = LakehouseTableFormat.SwapMidManifest;

    public LakehousePartitionStrategy LakehousePartitionStrategy { get; set; } = LakehousePartitionStrategy.None;

    public LakehouseCatalogType CatalogType { get; set; } = LakehouseCatalogType.None;

    public Guid? CatalogSinkId { get; set; }

    [StringLength(128)]
    public string? CatalogNamespace { get; set; }

    [StringLength(128)]
    public string? CatalogTableName { get; set; }

    public CdcSchemaDriftPolicy SchemaDriftPolicy { get; set; } = CdcSchemaDriftPolicy.Ignore;

    public bool InitialSnapshotEnabled { get; set; }

    [Range(10, 86400)]
    public int? MaxFreshnessLagSeconds { get; set; }

    [Range(1048576L, 107374182400L)]
    public long? MaxAllowedWalLagBytes { get; set; }

    [StringLength(512)]
    public string? RequiredColumns { get; set; }
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
    public bool SchemaDriftDetected { get; set; }
    public string? SchemaDriftSummary { get; set; }
    public string? CurrentSchemaHash { get; set; }
    public CdcMirrorCompatibilityDto? MirrorCompatibility { get; set; }
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
    [StringLength(128)]
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
    public string? ManifestDirectory { get; set; }
    public string? ManifestLatestKey { get; set; }
    public string? DeltaLogDirectory { get; set; }
    public long? LatestDeltaVersion { get; set; }
    public string? LatestManifestJson { get; set; }
    public string? LakehouseQuerySql { get; set; }
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

public class CdcAcceptSchemaResultDto
{
    public Guid StreamId { get; set; }
    public string SchemaBaselineHash { get; set; } = null!;
    public DateTime AcceptedAtUtc { get; set; }
    public string Message { get; set; } = null!;
}

public class CdcFieldMappingDto
{
    [Required]
    [StringLength(128)]
    public string SourceColumn { get; set; } = null!;

    [Required]
    [StringLength(128)]
    public string TargetColumn { get; set; } = null!;
}

public class CdcMirrorCompatibilityDto
{
    public bool Compatible { get; set; }
    public string? TargetProvider { get; set; }
    public string? CanonicalTargetTable { get; set; }
    public int SourceColumnCount { get; set; }
    public int TargetColumnCount { get; set; }
    public int MappedColumnCount { get; set; }
    public List<PreflightSchemaIssueDto> Issues { get; set; } = new();
}

public class InspectCdcMirrorSchemaInput
{
    [Required]
    public Guid SourceId { get; set; }

    public CdcSourceType SourceType { get; set; } = CdcSourceType.PostgreSqlLogicalReplication;

    [StringLength(2048)]
    public string? MonitoredTables { get; set; }

    [Required]
    public Guid TargetSinkId { get; set; }

    [Required]
    [StringLength(256)]
    public string TargetSinkDestination { get; set; } = null!;

    [StringLength(256)]
    public string? TargetSinkKeyColumns { get; set; }

    public CdcDeletePolicy TargetDeletePolicy { get; set; } = CdcDeletePolicy.DeleteRow;

    [StringLength(128)]
    public string? SoftDeleteColumn { get; set; }

    [StringLength(128)]
    public string? SoftDeleteTimestampColumn { get; set; }

    public List<CdcFieldMappingDto>? TargetFieldMappings { get; set; }
}

public class CdcMirrorSchemaInspectionDto
{
    public bool Compatible { get; set; }
    public string SourceProvider { get; set; } = "postgresql";
    public string? TargetProvider { get; set; }
    public string? CanonicalSourceTable { get; set; }
    public string? CanonicalTargetTable { get; set; }
    public List<PreflightColumnDto> SourceColumns { get; set; } = new();
    public List<PreflightColumnDto> TargetColumns { get; set; } = new();
    public List<CdcFieldMappingDto> SuggestedMappings { get; set; } = new();
    public List<PreflightSchemaIssueDto> Issues { get; set; } = new();
}

public class CompactCdcLakehouseInput
{
    [Range(2, 100)]
    public int MinFiles { get; set; } = 2;

    public bool DeleteCompactedFiles { get; set; } = true;
}

public class CdcLakehouseCompactionResultDto
{
    public Guid StreamId { get; set; }
    public bool Compacted { get; set; }
    public int CompactedFileCount { get; set; }
    public long ConsolidatedRowCount { get; set; }
    public string? OutputObjectKey { get; set; }
    public string? OutputManifestKey { get; set; }
    public long OutputByteSize { get; set; }
    public int DeletedSourceCount { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public string Message { get; set; } = null!;
}

public class GetCdcLakehouseHistoryInput
{
    [Range(0, long.MaxValue)]
    public long? Version { get; set; }

    [Range(1, 200)]
    public int MaxCommits { get; set; } = 32;
}

public class CdcDeltaCommitSummaryDto
{
    public long Version { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Operation { get; set; } = null!;
    public string? EngineInfo { get; set; }
    public string? SwapMidRunId { get; set; }
    public string? SnapshotMode { get; set; }
    public int AddedFileCount { get; set; }
    public int RemovedFileCount { get; set; }
    public long TotalRecordCount { get; set; }
    public string DeltaCommitKey { get; set; } = null!;
}

public class CdcDeltaActiveFileDto
{
    public string RelativePath { get; set; } = null!;
    public string ObjectKey { get; set; } = null!;
    public long SizeBytes { get; set; }
    public long? RecordCount { get; set; }
    public long AddedAtVersion { get; set; }
}

public class CdcLakehouseHistoryDto
{
    public Guid StreamId { get; set; }
    public string Destination { get; set; } = null!;
    public LakehouseTableFormat TableFormat { get; set; }
    public LakehousePartitionStrategy PartitionStrategy { get; set; }
    public string? DeltaLogDirectory { get; set; }
    public long? CurrentVersion { get; set; }
    public long? SelectedVersion { get; set; }
    public int TotalCommitCount { get; set; }
    public int SelectedActiveFileCount { get; set; }
    public long SelectedTotalBytes { get; set; }
    public long SelectedTotalRecordCount { get; set; }
    public List<CdcDeltaCommitSummaryDto> Commits { get; set; } = new();
    public List<CdcDeltaActiveFileDto> ActiveFiles { get; set; } = new();
    public string? TimeTravelQuerySql { get; set; }
    public string Summary { get; set; } = null!;
}

public class CdcCatalogSyncResultDto
{
    public Guid StreamId { get; set; }
    public LakehouseCatalogType CatalogType { get; set; }
    public bool Synced { get; set; }
    public long? SyncedVersion { get; set; }
    public string? CatalogTableIdentifier { get; set; }
    public string? LocationUri { get; set; }
    public DateTime SyncedAtUtc { get; set; }
    public string Message { get; set; } = null!;
}

public class GetCdcLakehousePreviewInput
{
    [Range(0, long.MaxValue)]
    public long? Version { get; set; }

    [Range(1, 50)]
    public int MaxRows { get; set; } = 25;
}

public class CdcLakehousePreviewDto
{
    public Guid StreamId { get; set; }
    public string Destination { get; set; } = null!;
    public LakehouseTableFormat TableFormat { get; set; }
    public string FileFormat { get; set; } = "parquet";
    public long? SelectedVersion { get; set; }
    public int ScannedFileCount { get; set; }
    public int TotalActiveFileCount { get; set; }
    public int MissingFileCount { get; set; }
    public int RowCount { get; set; }
    public bool Truncated { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, string?>> Rows { get; set; } = new();
    public List<string> SourceFiles { get; set; } = new();
    public string Summary { get; set; } = null!;
}

public class CdcContractViolationItemDto
{
    public string Type { get; set; } = null!;
    public string Severity { get; set; } = "error";
    public string Message { get; set; } = null!;
}

public class CdcContractEvaluationDto
{
    public Guid StreamId { get; set; }
    public string StreamName { get; set; } = null!;
    public CdcContractStatus Status { get; set; }
    public bool Violated { get; set; }
    public long? FreshnessLagSeconds { get; set; }
    public int? MaxFreshnessLagSeconds { get; set; }
    public long? WalLagBytes { get; set; }
    public long? MaxAllowedWalLagBytes { get; set; }
    public List<string> RequiredColumns { get; set; } = new();
    public List<string> MissingRequiredColumns { get; set; } = new();
    public bool SchemaDriftDetected { get; set; }
    public List<CdcContractViolationItemDto> Violations { get; set; } = new();
    public DateTime EvaluatedAtUtc { get; set; }
    public string Summary { get; set; } = null!;
}
