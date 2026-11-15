using System;
using System.Collections.Generic;
using System.Linq;

namespace SwapMid.DataIntegration;

/// <summary>
/// 平台内置连接器能力目录事实源（供 Schema 预检、前端表单与受控 MCP 工具统一消费）。
/// </summary>
public static class ConnectorCapabilityCatalog
{
    private static readonly IReadOnlyList<ConnectorCapabilityDto> All =
    [
        new ConnectorCapabilityDto
        {
            Provider = "postgresql",
            DisplayName = "PostgreSQL",
            DataSourceType = DataSourceType.RelationalDatabase,
            SupportsSourceRead = true,
            SupportsTargetWrite = true,
            SupportsAtomicUpsert = true,
            SupportsTargetReceiptV1 = true,
            RequiresStrictTlsForReceipt = true,
            SupportedWriteModes =
            [
                "append",
                "truncate",
                "upsert",
                "append-receipt-v1",
                "truncate-receipt-v1",
                "upsert-receipt-v1",
            ],
        },
        new ConnectorCapabilityDto
        {
            Provider = "sqlserver",
            DisplayName = "SQL Server",
            DataSourceType = DataSourceType.RelationalDatabase,
            SupportsSourceRead = true,
            SupportsTargetWrite = true,
            SupportsAtomicUpsert = true,
            SupportsTargetReceiptV1 = true,
            RequiresStrictTlsForReceipt = true,
            SupportedWriteModes =
            [
                "append",
                "truncate",
                "upsert",
                "append-receipt-v1",
                "truncate-receipt-v1",
                "upsert-receipt-v1",
            ],
        },
        new ConnectorCapabilityDto
        {
            Provider = "mysql",
            DisplayName = "MySQL / MariaDB",
            DataSourceType = DataSourceType.RelationalDatabase,
            SupportsSourceRead = true,
            SupportsTargetWrite = true,
            SupportsAtomicUpsert = true,
            SupportsTargetReceiptV1 = false,
            RequiresStrictTlsForReceipt = false,
            SupportedWriteModes =
            [
                "append",
                "truncate",
                "upsert",
            ],
        },
        new ConnectorCapabilityDto
        {
            Provider = "rest-api",
            DisplayName = "REST API",
            DataSourceType = DataSourceType.RestApi,
            SupportsSourceRead = true,
            SupportsTargetWrite = false,
            SupportsAtomicUpsert = false,
            SupportsTargetReceiptV1 = false,
            RequiresStrictTlsForReceipt = false,
            SupportedWriteModes = [],
        },
        new ConnectorCapabilityDto
        {
            Provider = "file",
            DisplayName = "CSV / Excel File",
            DataSourceType = DataSourceType.File,
            SupportsSourceRead = true,
            SupportsTargetWrite = false,
            SupportsAtomicUpsert = false,
            SupportsTargetReceiptV1 = false,
            RequiresStrictTlsForReceipt = false,
            SupportedWriteModes = [],
        },
        new ConnectorCapabilityDto
        {
            Provider = "clickhouse",
            DisplayName = "ClickHouse",
            DataSourceType = DataSourceType.ClickHouse,
            SupportsSourceRead = false,
            SupportsTargetWrite = true,
            SupportsAtomicUpsert = false,
            SupportsTargetReceiptV1 = false,
            RequiresStrictTlsForReceipt = false,
            SupportedWriteModes = new[] { "append", "truncate" },
        },
        new ConnectorCapabilityDto
        {
            Provider = "s3-parquet",
            DisplayName = "S3 / MinIO Object Storage",
            DataSourceType = DataSourceType.ObjectStorage,
            SupportsSourceRead = false,
            SupportsTargetWrite = true,
            SupportsAtomicUpsert = false,
            SupportsTargetReceiptV1 = false,
            RequiresStrictTlsForReceipt = false,
            SupportedWriteModes = new[] { "append" },
        },
        new ConnectorCapabilityDto
        {
            Provider = "kafka",
            DisplayName = "Apache Kafka",
            DataSourceType = DataSourceType.Kafka,
            SupportsSourceRead = false,
            SupportsTargetWrite = true,
            SupportsAtomicUpsert = false,
            SupportsTargetReceiptV1 = false,
            RequiresStrictTlsForReceipt = false,
            SupportedWriteModes = new[] { "append" },
        },
    ];

    public static IReadOnlyList<ConnectorCapabilityDto> GetAll() => All;

    public static ConnectorCapabilityDto? FindByProvider(string? provider)
        => string.IsNullOrWhiteSpace(provider)
            ? null
            : All.FirstOrDefault(item => string.Equals(item.Provider, provider.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string CanonicalProvider(DataSourceType type, string? rawProvider)
    {
        if (type == DataSourceType.RestApi) return "rest-api";
        if (type == DataSourceType.File) return "file";
        if (type == DataSourceType.Kafka) return "kafka";
        if (type == DataSourceType.ClickHouse) return "clickhouse";
        if (type == DataSourceType.ObjectStorage) return "s3-parquet";

        return rawProvider?.Trim().ToLowerInvariant() switch
        {
            "postgresql" or "postgres" or "npgsql" => "postgresql",
            "mysql" or "mariadb" => "mysql",
            "sqlite" => "sqlite",
            _ => "sqlserver",
        };
    }
}
