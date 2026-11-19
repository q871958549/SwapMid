namespace SwapMid.DataIntegration;

/// <summary>
/// PostgreSQL 逻辑复制（pgoutput）CDC 流字段长度与运行约束常量。
/// </summary>
public static class CdcStreamConsts
{
    public const int MaxNameLength = 128;
    public const int MaxDescriptionLength = 512;
    public const int MaxPostgresIdentifierLength = 63;
    public const int MaxMonitoredTablesLength = 2048;
    public const int MaxMonitoredTableCount = 64;
    public const int MaxTargetTopicNameLength = 128;
    public const int MaxTargetSinkDestinationLength = 256;
    public const int MaxTargetSinkKeyColumnsLength = 256;
    public const int MaxTargetSinkKeyColumnCount = 16;
    public const int MaxTargetFieldMappingsJsonLength = 16384;
    public const int MaxTargetFieldMappingCount = 256;
    public const int MaxSoftDeleteColumnLength = 128;
    public const int MaxSoftDeleteTimestampColumnLength = 128;
    public const int MaxCheckpointLsnLength = 128;
    public const int MaxErrorCodeLength = 64;
    public const int MaxErrorMessageLength = 2000;
    public const int MaxSchemaBaselineHashLength = 64;
    public const int MaxSchemaBaselineJsonLength = 32768;
    public const int MaxSchemaDriftSummaryLength = 1000;

    public const long WalLagWarningBytes = 100L * 1024 * 1024;

    public const int MinPollIntervalSeconds = 5;
    public const int DefaultPollIntervalSeconds = 15;
    public const int MaxPollIntervalSeconds = 3600;

    public const int MinBatchSize = 1;
    public const int DefaultBatchSize = 500;
    public const int MaxBatchSize = 5000;

    public const int MinLakehouseCompactMinFiles = 2;
    public const int MaxLakehouseCompactMinFiles = 100;

    public const int MaxCatalogNamespaceLength = 128;
    public const int MaxCatalogTableNameLength = 128;
    public const int MaxCatalogLastSyncStatusLength = 512;
    public const int DefaultLakehouseHistoryMaxCommits = 32;
    public const int MaxLakehouseHistoryMaxCommits = 200;

    public const int DefaultLakehousePreviewMaxRows = 25;
    public const int MaxLakehousePreviewMaxRows = 50;

    public const int MinFreshnessLagSeconds = 10;
    public const int MaxFreshnessLagSeconds = 86400;
    public const long MinAllowedWalLagBytes = 1024L * 1024L;
    public const long MaxAllowedWalLagBytes = 100L * 1024 * 1024 * 1024;
    public const int MaxRequiredColumnsLength = 512;
    public const int MaxRequiredColumnsCount = 64;
    public const int MaxContractViolationSummaryLength = 512;

    public const int MaxLakehouseWriteBranchLength = 64;
    public const int MaxLakehouseMaintenanceCronLength = 64;
    public const int MaxLakehouseLastMaintenanceSummaryLength = 512;
    public const int DefaultLakehouseExpireRetainLast = 5;
    public const int DefaultLakehouseExpireOlderThanMinutes = 1440;
}
