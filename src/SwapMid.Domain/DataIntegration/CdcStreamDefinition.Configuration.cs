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
        LakehouseTableFormat lakehouseTableFormat)
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

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
