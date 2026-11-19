using System;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace SwapMid.DataIntegration;

public partial class CdcStreamDefinition
{
    private static readonly Regex IcebergRefNamePattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>可选：Apache Iceberg v2 WAP 隔离写入目标分支（留空或 main 表示直接提交 main；如 staging 表示先写分支待审核后快进发布）。</summary>
    public string? LakehouseWriteBranch { get; private set; }

    /// <summary>可选：Apache Iceberg v2 定时维护（压缩 + 快照过期清理）5 段式 Cron 表达式。</summary>
    public string? LakehouseMaintenanceCron { get; private set; }

    /// <summary>定时快照过期策略至少保留的最近快照数（1..100）。</summary>
    public int? LakehouseExpireRetainLast { get; private set; }

    /// <summary>定时快照过期策略仅清理早于 N 分钟的快照（0..525600）。</summary>
    public int? LakehouseExpireOlderThanMinutes { get; private set; }

    /// <summary>定时快照过期策略是否同步物理删除已无任何引用（含命名标签/分支）的孤儿对象。</summary>
    public bool LakehouseDeleteOrphanedFiles { get; private set; }

    /// <summary>最近一次定时维护完成时间（UTC）。</summary>
    public DateTime? LakehouseLastMaintenanceAt { get; private set; }

    /// <summary>最近一次定时维护结果摘要。</summary>
    public string? LakehouseLastMaintenanceSummary { get; private set; }
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
        string? description,
        bool initialSnapshotEnabled,
        CdcDeletePolicy targetDeletePolicy,
        CdcSchemaDriftPolicy schemaDriftPolicy,
        string? targetFieldMappingsJson,
        string? softDeleteColumn,
        string? softDeleteTimestampColumn,
        CdcSourceType sourceType,
        int? lakehouseCompactMinFiles,
        LakehouseTableFormat lakehouseTableFormat,
        LakehouseCatalogType catalogType,
        Guid? catalogSinkId,
        string? catalogNamespace,
        string? catalogTableName)
    {
        var normalizedMonitoredTables = string.IsNullOrWhiteSpace(monitoredTables)
            ? null
            : Check.Length(monitoredTables.Trim(), nameof(monitoredTables), CdcStreamConsts.MaxMonitoredTablesLength);
        if (SourceId != Guid.Empty &&
            (SourceId != sourceId ||
             SourceType != sourceType ||
             !string.Equals(MonitoredTables, normalizedMonitoredTables, StringComparison.OrdinalIgnoreCase)))
        {
            SchemaBaselineHash = null;
            SchemaBaselineJson = null;
            ClearSchemaDrift();
        }

        Name = Check.NotNullOrWhiteSpace(name, nameof(name), CdcStreamConsts.MaxNameLength).Trim();
        SourceId = sourceId;
        SourceType = sourceType;
        PublicationName = Check.NotNullOrWhiteSpace(
            publicationName,
            nameof(publicationName),
            CdcStreamConsts.MaxPostgresIdentifierLength).Trim();
        SlotName = Check.NotNullOrWhiteSpace(
            slotName,
            nameof(slotName),
            CdcStreamConsts.MaxPostgresIdentifierLength).Trim();
        MonitoredTables = normalizedMonitoredTables;
        AutoCreatePublicationAndSlot = autoCreatePublicationAndSlot;
        InitialSnapshotEnabled = initialSnapshotEnabled;
        TargetDeletePolicy = targetMode == CdcTargetMode.RelationalTable
            ? targetDeletePolicy
            : CdcDeletePolicy.DeleteRow;
        SchemaDriftPolicy = schemaDriftPolicy;
        TargetFieldMappingsJson = targetMode == CdcTargetMode.RelationalTable
            ? CdcStreamMirrorConfigValidator.NormalizeFieldMappingsJson(targetFieldMappingsJson)
            : null;
        LakehouseCompactMinFiles = CdcStreamMirrorConfigValidator.NormalizeLakehouseCompactMinFiles(
            targetMode,
            lakehouseCompactMinFiles);
        LakehouseTableFormat = CdcStreamMirrorConfigValidator.NormalizeLakehouseTableFormat(
            targetMode,
            lakehouseTableFormat);
        if (LakehouseTableFormat != LakehouseTableFormat.ApacheIceberg)
        {
            LakehousePartitionStrategy = LakehousePartitionStrategy.None;
            LakehouseWriteMode = LakehouseWriteMode.AppendChangelog;
            LakehouseWriteBranch = null;
            LakehouseMaintenanceCron = null;
            LakehouseExpireRetainLast = null;
            LakehouseExpireOlderThanMinutes = null;
            LakehouseDeleteOrphanedFiles = false;
        }
        var normalizedCatalogType = CdcStreamMirrorConfigValidator.NormalizeCatalogType(
            targetMode,
            catalogType);
        var normalizedCatalogSinkId = normalizedCatalogType == LakehouseCatalogType.None
            ? null
            : catalogSinkId;
        var normalizedCatalogNamespace = normalizedCatalogType == LakehouseCatalogType.None
            ? null
            : CdcStreamMirrorConfigValidator.NormalizeCatalogIdentifier(
                catalogNamespace,
                CdcStreamConsts.MaxCatalogNamespaceLength,
                nameof(catalogNamespace));
        var normalizedCatalogTableName = normalizedCatalogType == LakehouseCatalogType.None
            ? null
            : CdcStreamMirrorConfigValidator.NormalizeCatalogIdentifier(
                catalogTableName,
                CdcStreamConsts.MaxCatalogTableNameLength,
                nameof(catalogTableName));
        if (CatalogType != normalizedCatalogType ||
            CatalogSinkId != normalizedCatalogSinkId ||
            !string.Equals(CatalogNamespace, normalizedCatalogNamespace, StringComparison.Ordinal) ||
            !string.Equals(CatalogTableName, normalizedCatalogTableName, StringComparison.Ordinal))
        {
            CatalogLastSyncedVersion = null;
            CatalogLastSyncedAt = null;
            CatalogLastSyncStatus = null;
        }

        CatalogType = normalizedCatalogType;
        CatalogSinkId = normalizedCatalogSinkId;
        CatalogNamespace = normalizedCatalogNamespace;
        CatalogTableName = normalizedCatalogTableName;
        var enableSoftDelete = targetMode == CdcTargetMode.RelationalTable && TargetDeletePolicy == CdcDeletePolicy.SoftDelete;
        SoftDeleteColumn = enableSoftDelete
            ? CdcStreamMirrorConfigValidator.NormalizeIdentifier(
                softDeleteColumn,
                CdcStreamConsts.MaxSoftDeleteColumnLength,
                nameof(softDeleteColumn))
            : null;
        SoftDeleteTimestampColumn = enableSoftDelete
            ? CdcStreamMirrorConfigValidator.NormalizeIdentifier(
                softDeleteTimestampColumn,
                CdcStreamConsts.MaxSoftDeleteTimestampColumnLength,
                nameof(softDeleteTimestampColumn))
            : null;
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

        CdcStreamMirrorConfigValidator.ValidateMirrorColumns(
            TargetMode,
            TargetSinkKeyColumns,
            TargetFieldMappingsJson,
            TargetDeletePolicy,
            SoftDeleteColumn,
            SoftDeleteTimestampColumn);
    }

    public void ConfigureLakehousePartitionStrategy(LakehousePartitionStrategy strategy)
    {
        LakehousePartitionStrategy = TargetMode == CdcTargetMode.ExternalSink &&
                                     LakehouseTableFormat == LakehouseTableFormat.ApacheIceberg
            ? strategy
            : LakehousePartitionStrategy.None;
    }

    public void ConfigureLakehouseWriteMode(LakehouseWriteMode mode)
    {
        if (TargetMode != CdcTargetMode.ExternalSink ||
            LakehouseTableFormat != LakehouseTableFormat.ApacheIceberg)
        {
            LakehouseWriteMode = LakehouseWriteMode.AppendChangelog;
            return;
        }

        if (mode == LakehouseWriteMode.MergeOnReadUpsert &&
            string.IsNullOrWhiteSpace(TargetSinkKeyColumns))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "启用 Apache Iceberg v2 Merge-on-Read 主键 Upsert 模式必须填写业务主键列（TargetSinkKeyColumns）。");
        }

        LakehouseWriteMode = mode;
    }

    public bool HasDataContractConfigured =>
        MaxFreshnessLagSeconds.HasValue ||
        MaxAllowedWalLagBytes.HasValue ||
        !string.IsNullOrWhiteSpace(RequiredColumns);

    public void ConfigureDataContract(
        int? maxFreshnessLagSeconds,
        long? maxAllowedWalLagBytes,
        string? requiredColumns)
    {
        var normalizedFreshness = CdcStreamMirrorConfigValidator.NormalizeMaxFreshnessLagSeconds(maxFreshnessLagSeconds);
        var normalizedWalBudget = CdcStreamMirrorConfigValidator.NormalizeMaxAllowedWalLagBytes(maxAllowedWalLagBytes);
        var normalizedRequired = CdcStreamMirrorConfigValidator.NormalizeRequiredColumns(requiredColumns);

        var changed = MaxFreshnessLagSeconds != normalizedFreshness ||
                      MaxAllowedWalLagBytes != normalizedWalBudget ||
                      !string.Equals(RequiredColumns, normalizedRequired, StringComparison.OrdinalIgnoreCase);

        MaxFreshnessLagSeconds = normalizedFreshness;
        MaxAllowedWalLagBytes = normalizedWalBudget;
        RequiredColumns = normalizedRequired;

        if (!HasDataContractConfigured)
        {
            ContractViolated = false;
            ContractViolationSummary = null;
            ContractLastEvaluatedAt = null;
        }
        else if (changed)
        {
            ContractViolated = false;
            ContractViolationSummary = null;
        }
    }

    public void RecordContractEvaluation(
        bool violated,
        string? summary,
        DateTime evaluatedAtUtc)
    {
        ContractViolated = violated;
        ContractViolationSummary = string.IsNullOrWhiteSpace(summary)
            ? null
            : Truncate(summary.Trim(), CdcStreamConsts.MaxContractViolationSummaryLength);
        ContractLastEvaluatedAt = evaluatedAtUtc;
    }

    public void ConfigureLakehouseWapAndMaintenance(
        string? writeBranch,
        string? maintenanceCron,
        int? expireRetainLast,
        int? expireOlderThanMinutes,
        bool deleteOrphanedFiles)
    {
        if (TargetMode != CdcTargetMode.ExternalSink ||
            LakehouseTableFormat != LakehouseTableFormat.ApacheIceberg)
        {
            LakehouseWriteBranch = null;
            LakehouseMaintenanceCron = null;
            LakehouseExpireRetainLast = null;
            LakehouseExpireOlderThanMinutes = null;
            LakehouseDeleteOrphanedFiles = false;
            return;
        }

        var trimmedBranch = writeBranch?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedBranch) ||
            string.Equals(trimmedBranch, "main", StringComparison.OrdinalIgnoreCase))
        {
            LakehouseWriteBranch = null;
        }
        else
        {
            if (!IcebergRefNamePattern.IsMatch(trimmedBranch))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "Apache Iceberg v2 WAP 目标分支名必须由字母/数字开头，仅含字母、数字、点、下划线与连字符（1-64 字符）。");
            }

            LakehouseWriteBranch = trimmedBranch;
        }

        var trimmedCron = string.IsNullOrWhiteSpace(maintenanceCron)
            ? null
            : Check.Length(maintenanceCron.Trim(), nameof(maintenanceCron), CdcStreamConsts.MaxLakehouseMaintenanceCronLength);
        LakehouseMaintenanceCron = trimmedCron;
        LakehouseExpireRetainLast = trimmedCron != null
            ? Math.Clamp(expireRetainLast ?? CdcStreamConsts.DefaultLakehouseExpireRetainLast, 1, 100)
            : (expireRetainLast.HasValue ? Math.Clamp(expireRetainLast.Value, 1, 100) : null);
        LakehouseExpireOlderThanMinutes = trimmedCron != null
            ? Math.Clamp(expireOlderThanMinutes ?? CdcStreamConsts.DefaultLakehouseExpireOlderThanMinutes, 0, 525_600)
            : (expireOlderThanMinutes.HasValue ? Math.Clamp(expireOlderThanMinutes.Value, 0, 525_600) : null);
        LakehouseDeleteOrphanedFiles = trimmedCron != null && deleteOrphanedFiles;
    }

    public void RecordLakehouseMaintenance(DateTime completedAtUtc, string summary)
    {
        LakehouseLastMaintenanceAt = completedAtUtc;
        LakehouseLastMaintenanceSummary = Truncate(
            summary?.Trim() ?? string.Empty,
            CdcStreamConsts.MaxLakehouseLastMaintenanceSummaryLength);
    }

    public void AcceptInferredSchemaDrift()
    {
        SchemaBaselineHash = null;
        SchemaBaselineJson = null;
        ClearSchemaDrift();
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
