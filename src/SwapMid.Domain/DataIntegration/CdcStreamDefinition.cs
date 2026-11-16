using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.DataIntegration;

/// <summary>
/// 轻量级 PostgreSQL 逻辑复制（pgoutput）CDC 流定义。
/// 通过只读 WAL 复制槽周期性增量读取已提交变更，保持单调递增 LSN 断点，并投递至消息总线或关联数据管道。
/// </summary>
public class CdcStreamDefinition : FullAuditedAggregateRoot<Guid>
{
    /// <summary>流名称（唯一）。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>流描述（可空）。</summary>
    public string? Description { get; private set; }

    /// <summary>上游 PostgreSQL 数据源 Id。</summary>
    public Guid SourceId { get; private set; }

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
        string? targetSinkKeyColumns = null)
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
            description);
        Status = CdcStreamStatus.Draft;
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
        string? targetSinkKeyColumns = null)
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
            description);
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

    private void ApplyConfiguration(
        string name,
        Guid sourceId,
        string publicationName,
        string slotName,
        CdcTargetMode targetMode,
        string? targetTopicName,
        Guid? targetPipelineId,
        Guid? targetSinkId,
        string? targetSinkDestination,
        string? targetSinkKeyColumns,
        string? monitoredTables,
        bool autoCreatePublicationAndSlot,
        int pollIntervalSeconds,
        int maxBatchSize,
        string? description)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), CdcStreamConsts.MaxNameLength).Trim();
        SourceId = sourceId;
        PublicationName = Check.NotNullOrWhiteSpace(
            publicationName,
            nameof(publicationName),
            CdcStreamConsts.MaxPostgresIdentifierLength).Trim();
        SlotName = Check.NotNullOrWhiteSpace(
            slotName,
            nameof(slotName),
            CdcStreamConsts.MaxPostgresIdentifierLength).Trim();
        MonitoredTables = string.IsNullOrWhiteSpace(monitoredTables)
            ? null
            : Check.Length(monitoredTables.Trim(), nameof(monitoredTables), CdcStreamConsts.MaxMonitoredTablesLength);
        AutoCreatePublicationAndSlot = autoCreatePublicationAndSlot;
        PollIntervalSeconds = Math.Clamp(
            pollIntervalSeconds,
            CdcStreamConsts.MinPollIntervalSeconds,
            CdcStreamConsts.MaxPollIntervalSeconds);
        MaxBatchSize = Math.Clamp(
            maxBatchSize,
            CdcStreamConsts.MinBatchSize,
            CdcStreamConsts.MaxBatchSize);
        TargetMode = targetMode;
        TargetTopicName = string.IsNullOrWhiteSpace(targetTopicName)
            ? null
            : Check.Length(targetTopicName.Trim(), nameof(targetTopicName), CdcStreamConsts.MaxTargetTopicNameLength);
        TargetPipelineId = targetPipelineId;
        TargetSinkId = targetSinkId;
        TargetSinkDestination = string.IsNullOrWhiteSpace(targetSinkDestination)
            ? null
            : Check.Length(
                targetSinkDestination.Trim(),
                nameof(targetSinkDestination),
                CdcStreamConsts.MaxTargetSinkDestinationLength);
        TargetSinkKeyColumns = string.IsNullOrWhiteSpace(targetSinkKeyColumns)
            ? null
            : Check.Length(
                targetSinkKeyColumns.Trim(),
                nameof(targetSinkKeyColumns),
                CdcStreamConsts.MaxTargetSinkKeyColumnsLength);
        Description = string.IsNullOrWhiteSpace(description)
            ? null
            : Check.Length(description.Trim(), nameof(description), CdcStreamConsts.MaxDescriptionLength);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
