using System;
using Volo.Abp;

namespace SwapMid.DataIntegration;

public partial class CdcStreamDefinition
{
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

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
