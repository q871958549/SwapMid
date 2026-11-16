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
    public const int MaxCheckpointLsnLength = 32;
    public const int MaxErrorCodeLength = 64;
    public const int MaxErrorMessageLength = 2000;

    public const long WalLagWarningBytes = 100L * 1024 * 1024;

    public const int MinPollIntervalSeconds = 5;
    public const int DefaultPollIntervalSeconds = 15;
    public const int MaxPollIntervalSeconds = 3600;

    public const int MinBatchSize = 1;
    public const int DefaultBatchSize = 500;
    public const int MaxBatchSize = 5000;
}
