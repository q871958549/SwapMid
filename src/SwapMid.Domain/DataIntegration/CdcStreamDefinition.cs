using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.DataIntegration;

/// <summary>
/// 轻量级多源（PostgreSQL 逻辑复制 / SQL Server CT / MySQL Binlog）CDC 流定义。
/// 周期性增量读取已提交变更，保持单调断点，并投递至消息总线、联动管道、分析接收端或关系表镜像。
/// </summary>
public partial class CdcStreamDefinition : FullAuditedAggregateRoot<Guid>
{
    /// <summary>流名称（唯一）。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>流描述（可空）。</summary>
    public string? Description { get; private set; }

    /// <summary>上游 PostgreSQL 数据源 Id。</summary>
    public Guid SourceId { get; private set; }

    /// <summary>上游 CDC 捕获协议类型（PG pgoutput / SQL Server Change Tracking）。</summary>
    public CdcSourceType SourceType { get; private set; }

    /// <summary>PostgreSQL 逻辑复制发布（Publication）名称。</summary>
    public string PublicationName { get; private set; } = null!;

    /// <summary>PostgreSQL 逻辑复制槽（Replication Slot）名称。</summary>
    public string SlotName { get; private set; } = null!;

    /// <summary>监听表清单（逗号分隔的 schema.table，留空表示监听发布内全部表）。</summary>
    public string? MonitoredTables { get; private set; }

    /// <summary>若发布或复制槽缺失，是否在预检/首次捕获时自动创建。</summary>
    public bool AutoCreatePublicationAndSlot { get; private set; }

    /// <summary>后台 Hangfire 轮询周期（秒，5~3600）。</summary>
    public int PollIntervalSeconds { get; private set; }

    /// <summary>单次有界捕获最大变更事件数（1~5000）。</summary>
    public int MaxBatchSize { get; private set; }

    /// <summary>下游投递目标类型（MessageBus 主题或数据管道）。</summary>
    public CdcTargetMode TargetMode { get; private set; }

    /// <summary>当 TargetMode = MessageBusTopic 时的目标主题名称。</summary>
    public string? TargetTopicName { get; private set; }

    /// <summary>当 TargetMode = DataPipeline 时联动触发的数据管道 Id。</summary>
    public Guid? TargetPipelineId { get; private set; }

    /// <summary>当 TargetMode = ExternalSink 时的外部接收端数据源 Id（Kafka / ClickHouse / 对象存储）。</summary>
    public Guid? TargetSinkId { get; private set; }

    /// <summary>当 TargetMode = ExternalSink 时的下游目标标识（Kafka Topic / ClickHouse 库表 / 对象前缀）。</summary>
    public string? TargetSinkDestination { get; private set; }

    /// <summary>当 TargetMode = ExternalSink 时的分区主键列名列表（逗号分隔，用于 Kafka 同主键保序投递）。</summary>
    public string? TargetSinkKeyColumns { get; private set; }

    /// <summary>关系型目标表镜像（RelationalTable）遇到上游 DELETE 时的处理策略。</summary>
    public CdcDeletePolicy TargetDeletePolicy { get; private set; }

    /// <summary>关系型目标表镜像（RelationalTable）显式字段映射 JSON（源列到目标列）。</summary>
    public string? TargetFieldMappingsJson { get; private set; }

    /// <summary>软删除策略下的布尔或整型墓碑标记列名（如 is_deleted）。</summary>
    public string? SoftDeleteColumn { get; private set; }

    /// <summary>软删除策略下的可选删除时间戳列名（如 deleted_at_utc）。</summary>
    public string? SoftDeleteTimestampColumn { get; private set; }

    /// <summary>ObjectStorage 接收端 Lakehouse 触发自动压缩合并的分片阈值（null 表示仅手动触发）。</summary>
    public int? LakehouseCompactMinFiles { get; private set; }

    /// <summary>ObjectStorage 接收端 Lakehouse 开放表格式提交协议（SwapMidManifest / DeltaLake）。</summary>
    public LakehouseTableFormat LakehouseTableFormat { get; private set; }

    /// <summary>外部开放元数据目录同步协议（None / UnityCatalogRest / IcebergRest）。</summary>
    public LakehouseCatalogType CatalogType { get; private set; }

    /// <summary>关联外部 Catalog REST 服务凭证的 RestApi DataSource ID。</summary>
    public Guid? CatalogSinkId { get; private set; }

    /// <summary>外部 Catalog 中的命名空间（Unity Catalog 为 schema 或 catalog.schema；Iceberg 为 namespace）。</summary>
    public string? CatalogNamespace { get; private set; }

    /// <summary>外部 Catalog 中的注册表名。</summary>
    public string? CatalogTableName { get; private set; }

    /// <summary>最近一次成功同步到外部 Catalog 的 Delta/Manifest 版本号。</summary>
    public long? CatalogLastSyncedVersion { get; private set; }

    /// <summary>最近一次成功同步到外部 Catalog 的时间（UTC）。</summary>
    public DateTime? CatalogLastSyncedAt { get; private set; }

    /// <summary>最近一次外部 Catalog 同步结果摘要或脱敏诊断消息。</summary>
    public string? CatalogLastSyncStatus { get; private set; }

    /// <summary>上游监控表发生 DDL/Schema 漂移时的护栏策略。</summary>
    public CdcSchemaDriftPolicy SchemaDriftPolicy { get; private set; }

    /// <summary>最近一次已确认的上游监控表 Schema 指纹（SHA-256 十六进制）。</summary>
    public string? SchemaBaselineHash { get; private set; }

    /// <summary>最近一次已确认的上游监控表结构基线 JSON。</summary>
    public string? SchemaBaselineJson { get; private set; }

    /// <summary>当前是否存在尚未确认消警的上游 Schema 漂移。</summary>
    public bool SchemaDriftDetected { get; private set; }

    /// <summary>最近一次检测到的上游 Schema 变更摘要。</summary>
    public string? SchemaDriftSummary { get; private set; }

    /// <summary>最近一次检测到上游 Schema 漂移时间（UTC）。</summary>
    public DateTime? SchemaDriftDetectedAt { get; private set; }

    /// <summary>是否在首次复制前自动通过 PG EXPORT_SNAPSHOT 执行一次全量一致性基线快照。</summary>
    public bool InitialSnapshotEnabled { get; private set; }

    /// <summary>初始全量快照执行状态。</summary>
    public CdcSnapshotStatus SnapshotStatus { get; private set; }

    /// <summary>最近一次全量一致性快照完成时间（UTC）。</summary>
    public DateTime? SnapshotCompletedAt { get; private set; }

    /// <summary>累计通过初始/重同步快照导出的记录总数。</summary>
    public long SnapshotRowCount { get; private set; }

    /// <summary>运行状态（草稿 / 启用 / 暂停 / 异常）。</summary>
    public CdcStreamStatus Status { get; private set; }

    /// <summary>已确认持久化投递后的单调 LSN 检查点（PostgreSQL pg_lsn 文本形式，如 0/16A2B88）。</summary>
    public string? CheckpointLsn { get; private set; }

    /// <summary>最近一次成功执行捕获的时间（UTC）。</summary>
    public DateTime? LastCapturedAt { get; private set; }

    /// <summary>最近一次捕获投递的变更事件条数。</summary>
    public long LastCapturedCount { get; private set; }

    /// <summary>累计成功捕获并确认投递的变更事件总数。</summary>
    public long TotalCapturedCount { get; private set; }

    /// <summary>最近一次捕获异常错误码。</summary>
    public string? LastErrorCode { get; private set; }

    /// <summary>最近一次捕获异常摘要。</summary>
    public string? LastErrorMessage { get; private set; }

    private CdcStreamDefinition() { }

    public CdcStreamDefinition(
        Guid id,
        string name,
        Guid sourceId,
        string publicationName,
        string slotName,
        CdcTargetMode targetMode,
        string? targetTopicName = null,
        Guid? targetPipelineId = null,
        string? monitoredTables = null,
        bool autoCreatePublicationAndSlot = true,
        int pollIntervalSeconds = CdcStreamConsts.DefaultPollIntervalSeconds,
        int maxBatchSize = CdcStreamConsts.DefaultBatchSize,
        string? description = null,
        Guid? targetSinkId = null,
        string? targetSinkDestination = null,
        string? targetSinkKeyColumns = null,
        bool initialSnapshotEnabled = false,
        CdcDeletePolicy targetDeletePolicy = CdcDeletePolicy.DeleteRow,
        CdcSchemaDriftPolicy schemaDriftPolicy = CdcSchemaDriftPolicy.Ignore,
        string? targetFieldMappingsJson = null,
        string? softDeleteColumn = null,
        string? softDeleteTimestampColumn = null,
        CdcSourceType sourceType = CdcSourceType.PostgreSqlLogicalReplication,
        int? lakehouseCompactMinFiles = null,
        LakehouseTableFormat lakehouseTableFormat = LakehouseTableFormat.SwapMidManifest,
        LakehouseCatalogType catalogType = LakehouseCatalogType.None,
        Guid? catalogSinkId = null,
        string? catalogNamespace = null,
        string? catalogTableName = null)
        : base(id)
    {
        ApplyConfiguration(
            name,
            sourceId,
            publicationName,
            slotName,
            targetMode,
            targetTopicName,
            targetPipelineId,
            targetSinkId,
            targetSinkDestination,
            targetSinkKeyColumns,
            monitoredTables,
            autoCreatePublicationAndSlot,
            pollIntervalSeconds,
            maxBatchSize,
            description,
            initialSnapshotEnabled,
            targetDeletePolicy,
            schemaDriftPolicy,
            targetFieldMappingsJson,
            softDeleteColumn,
            softDeleteTimestampColumn,
            sourceType,
            lakehouseCompactMinFiles,
            lakehouseTableFormat,
            catalogType,
            catalogSinkId,
            catalogNamespace,
            catalogTableName);
        Status = CdcStreamStatus.Draft;
        SnapshotStatus = CdcSnapshotStatus.NotStarted;
    }

    public void Update(
        string name,
        Guid sourceId,
        string publicationName,
        string slotName,
        CdcTargetMode targetMode,
        string? targetTopicName,
        Guid? targetPipelineId,
        string? monitoredTables,
        bool autoCreatePublicationAndSlot,
        int pollIntervalSeconds,
        int maxBatchSize,
        string? description,
        Guid? targetSinkId = null,
        string? targetSinkDestination = null,
        string? targetSinkKeyColumns = null,
        bool initialSnapshotEnabled = false,
        CdcDeletePolicy targetDeletePolicy = CdcDeletePolicy.DeleteRow,
        CdcSchemaDriftPolicy schemaDriftPolicy = CdcSchemaDriftPolicy.Ignore,
        string? targetFieldMappingsJson = null,
        string? softDeleteColumn = null,
        string? softDeleteTimestampColumn = null,
        CdcSourceType sourceType = CdcSourceType.PostgreSqlLogicalReplication,
        int? lakehouseCompactMinFiles = null,
        LakehouseTableFormat lakehouseTableFormat = LakehouseTableFormat.SwapMidManifest,
        LakehouseCatalogType catalogType = LakehouseCatalogType.None,
        Guid? catalogSinkId = null,
        string? catalogNamespace = null,
        string? catalogTableName = null)
    {
        ApplyConfiguration(
            name,
            sourceId,
            publicationName,
            slotName,
            targetMode,
            targetTopicName,
            targetPipelineId,
            targetSinkId,
            targetSinkDestination,
            targetSinkKeyColumns,
            monitoredTables,
            autoCreatePublicationAndSlot,
            pollIntervalSeconds,
            maxBatchSize,
            description,
            initialSnapshotEnabled,
            targetDeletePolicy,
            schemaDriftPolicy,
            targetFieldMappingsJson,
            softDeleteColumn,
            softDeleteTimestampColumn,
            sourceType,
            lakehouseCompactMinFiles,
            lakehouseTableFormat,
            catalogType,
            catalogSinkId,
            catalogNamespace,
            catalogTableName);
    }

    public void Activate()
    {
        Status = CdcStreamStatus.Active;
        LastErrorCode = null;
        LastErrorMessage = null;
    }

    public void Pause()
    {
        Status = CdcStreamStatus.Paused;
    }

    public void MarkCaptureSucceeded(
        string? newCheckpointLsn,
        long capturedCount,
        DateTime capturedAtUtc)
    {
        if (!string.IsNullOrWhiteSpace(newCheckpointLsn))
        {
            CheckpointLsn = Truncate(newCheckpointLsn.Trim(), CdcStreamConsts.MaxCheckpointLsnLength);
        }

        LastCapturedAt = capturedAtUtc;
        LastCapturedCount = capturedCount;
        if (capturedCount > 0)
        {
            TotalCapturedCount += capturedCount;
        }

        LastErrorCode = null;
        LastErrorMessage = null;
        if (Status == CdcStreamStatus.Error)
        {
            Status = CdcStreamStatus.Active;
        }
    }

    public void MarkCaptureFailed(string errorCode, string errorMessage)
    {
        Status = CdcStreamStatus.Error;
        LastErrorCode = Truncate(errorCode, CdcStreamConsts.MaxErrorCodeLength);
        LastErrorMessage = Truncate(errorMessage, CdcStreamConsts.MaxErrorMessageLength);
    }

    public void ResetCheckpoint(string? checkpointLsn)
    {
        CheckpointLsn = string.IsNullOrWhiteSpace(checkpointLsn)
            ? null
            : Truncate(checkpointLsn.Trim(), CdcStreamConsts.MaxCheckpointLsnLength);
        LastErrorCode = null;
        LastErrorMessage = null;
        if (Status == CdcStreamStatus.Error)
        {
            Status = CdcStreamStatus.Paused;
        }
    }

    public void MarkSnapshotSucceeded(string? consistentPointLsn, long rowCount, DateTime completedAtUtc)
    {
        if (!string.IsNullOrWhiteSpace(consistentPointLsn) && string.IsNullOrWhiteSpace(CheckpointLsn))
        {
            CheckpointLsn = Truncate(consistentPointLsn.Trim(), CdcStreamConsts.MaxCheckpointLsnLength);
        }

        SnapshotStatus = CdcSnapshotStatus.Completed;
        SnapshotCompletedAt = completedAtUtc;
        if (rowCount > 0)
        {
            SnapshotRowCount += rowCount;
            TotalCapturedCount += rowCount;
        }

        LastCapturedAt = completedAtUtc;
        LastCapturedCount = rowCount;
        LastErrorCode = null;
        LastErrorMessage = null;
    }

    public void MarkSnapshotFailed(string errorCode, string errorMessage)
    {
        SnapshotStatus = CdcSnapshotStatus.Failed;
        LastErrorCode = Truncate(errorCode, CdcStreamConsts.MaxErrorCodeLength);
        LastErrorMessage = Truncate(errorMessage, CdcStreamConsts.MaxErrorMessageLength);
    }

    public void ResetSnapshot()
    {
        SnapshotStatus = CdcSnapshotStatus.NotStarted;
        SnapshotCompletedAt = null;
    }

    public void UpdateSchemaBaseline(string hash, string baselineJson)
    {
        SchemaBaselineHash = Check.NotNullOrWhiteSpace(
            hash,
            nameof(hash),
            CdcStreamConsts.MaxSchemaBaselineHashLength).Trim();
        SchemaBaselineJson = Check.NotNullOrWhiteSpace(
            baselineJson,
            nameof(baselineJson),
            CdcStreamConsts.MaxSchemaBaselineJsonLength);
        ClearSchemaDrift();
    }

    public void MarkSchemaDriftDetected(
        string summary,
        DateTime detectedAtUtc,
        bool pauseStream,
        string? errorCode = null)
    {
        SchemaDriftDetected = true;
        SchemaDriftSummary = Truncate(summary, CdcStreamConsts.MaxSchemaDriftSummaryLength);
        SchemaDriftDetectedAt = detectedAtUtc;
        if (pauseStream)
        {
            Status = CdcStreamStatus.Paused;
            LastErrorCode = Truncate(
                errorCode ?? SwapMidDomainErrorCodes.DataIntegration.CdcStreamSchemaDriftDetected,
                CdcStreamConsts.MaxErrorCodeLength);
            LastErrorMessage = Truncate(summary, CdcStreamConsts.MaxErrorMessageLength);
        }
    }

    public void ClearSchemaDrift()
    {
        SchemaDriftDetected = false;
        SchemaDriftSummary = null;
        SchemaDriftDetectedAt = null;
        if (string.Equals(
                LastErrorCode,
                SwapMidDomainErrorCodes.DataIntegration.CdcStreamSchemaDriftDetected,
                StringComparison.Ordinal))
        {
            LastErrorCode = null;
            LastErrorMessage = null;
        }
    }

    public void RecordCatalogSyncAttempt(
        bool succeeded,
        long? syncedVersion,
        DateTime attemptAtUtc,
        string status)
    {
        if (succeeded)
        {
            CatalogLastSyncedVersion = syncedVersion ?? CatalogLastSyncedVersion;
            CatalogLastSyncedAt = attemptAtUtc;
        }

        CatalogLastSyncStatus = Truncate(
            status?.Trim() ?? string.Empty,
            CdcStreamConsts.MaxCatalogLastSyncStatusLength);
    }
}
