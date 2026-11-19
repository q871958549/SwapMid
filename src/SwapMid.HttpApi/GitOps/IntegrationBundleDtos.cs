using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SwapMid.ApiManagement;
using SwapMid.DataIntegration;
using SwapMid.MessageBus;

namespace SwapMid.Controllers.GitOps;

/// <summary>声明式 GitOps 集成清单根文档（swapmid.io/v1，零明文密钥，跨环境可移植）</summary>
public class IntegrationBundleDocument
{
    /// <summary>清单规范版本（固定 swapmid.io/v1）</summary>
    public string SchemaVersion { get; set; } = "swapmid.io/v1";

    /// <summary>导出时的 SwapMid 平台版本</summary>
    public string PlatformVersion { get; set; } = "2.8.0";

    /// <summary>导出时间（UTC ISO-8601 字符串）</summary>
    public string ExportedAtUtc { get; set; } = DateTime.UtcNow.ToString("O");

    /// <summary>数据源声明列表（已脱敏，不含明文密码/密钥）</summary>
    public List<BundleDataSourceItem> DataSources { get; set; } = new();

    /// <summary>数据管道声明列表（按数据源名称引用）</summary>
    public List<BundleDataPipelineItem> DataPipelines { get; set; } = new();

    /// <summary>CDC 实时流声明列表（按数据源/管道名称引用）</summary>
    public List<BundleCdcStreamItem> CdcStreams { get; set; } = new();

    /// <summary>消息主题声明列表</summary>
    public List<BundleMessageTopicItem> MessageTopics { get; set; } = new();

    /// <summary>消息订阅声明列表（按主题名称引用，不含明文签名密钥）</summary>
    public List<BundleMessageSubscriptionItem> MessageSubscriptions { get; set; } = new();

    /// <summary>API 网关分组声明列表</summary>
    public List<BundleApiGroupItem> ApiGroups { get; set; } = new();

    /// <summary>API 网关路由声明列表（按分组名称引用）</summary>
    public List<BundleApiRouteItem> ApiRoutes { get; set; } = new();
}

/// <summary>声明式数据源条目（零明文凭据）</summary>
public class BundleDataSourceItem
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DataSourceType Type { get; set; }
    public string Config { get; set; } = "{}";
    public bool HasSecret { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>声明式数据管道条目</summary>
public class BundleDataPipelineItem
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string? TargetName { get; set; }
    public string? TransformConfig { get; set; }
    public string? ScheduleCron { get; set; }
    public bool ScheduleEnabled { get; set; }
}

/// <summary>声明式 CDC 实时流条目</summary>
public class BundleCdcStreamItem
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public CdcSourceType SourceType { get; set; }
    public string? PublicationName { get; set; }
    public string? SlotName { get; set; }
    public string? MonitoredTables { get; set; }
    public bool AutoCreatePublicationAndSlot { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 15;
    public int MaxBatchSize { get; set; } = 500;
    public CdcTargetMode TargetMode { get; set; }
    public string? TargetTopicName { get; set; }
    public string? TargetPipelineName { get; set; }
    public string? TargetSinkName { get; set; }
    public string? TargetSinkDestination { get; set; }
    public string? TargetSinkKeyColumns { get; set; }
    public CdcDeletePolicy TargetDeletePolicy { get; set; }
    public LakehouseTableFormat LakehouseTableFormat { get; set; }
    public LakehousePartitionStrategy LakehousePartitionStrategy { get; set; }
    public LakehouseWriteMode LakehouseWriteMode { get; set; }
    public string? LakehouseWriteBranch { get; set; }
    public string? LakehouseMaintenanceCron { get; set; }
    public int? LakehouseExpireRetainLast { get; set; }
    public int? LakehouseExpireOlderThanMinutes { get; set; }
    public bool LakehouseDeleteOrphanedFiles { get; set; }
    public int? LakehouseCompactMinFiles { get; set; }
    public LakehouseCatalogType CatalogType { get; set; }
    public string? CatalogSinkName { get; set; }
    public string? CatalogNamespace { get; set; }
    public string? CatalogTableName { get; set; }
    public CdcSchemaDriftPolicy SchemaDriftPolicy { get; set; }
    public bool InitialSnapshotEnabled { get; set; }
    public int? MaxFreshnessLagSeconds { get; set; }
    public long? MaxAllowedWalLagBytes { get; set; }
    public string? RequiredColumns { get; set; }
}

/// <summary>声明式消息主题条目</summary>
public class BundleMessageTopicItem
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TopicType Type { get; set; }
    public int RetryCount { get; set; } = 3;
    public int RetryIntervalSeconds { get; set; } = 30;
    public bool IsEnabled { get; set; } = true;
}

/// <summary>声明式消息订阅条目</summary>
public class BundleMessageSubscriptionItem
{
    public string Name { get; set; } = string.Empty;
    public string TopicName { get; set; } = string.Empty;
    public DeliveryType DeliveryType { get; set; }
    public string? EndpointUrl { get; set; }
    public string? ExchangeName { get; set; }
    public string? RoutingKey { get; set; }
    public string? FilterRule { get; set; }
    public bool HasSecret { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>声明式 API 分组条目</summary>
public class BundleApiGroupItem
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Order { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>声明式 API 路由条目</summary>
public class BundleApiRouteItem
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? GroupName { get; set; }
    public string Path { get; set; } = string.Empty;
    public HttpMethodType HttpMethod { get; set; }
    public string UpstreamUrl { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>声明式集成清单导出结果</summary>
public class IntegrationBundleExportResultDto
{
    public string SchemaVersion { get; set; } = "swapmid.io/v1";
    public string Format { get; set; } = "yaml";
    public int ResourceCount { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string Content { get; set; } = string.Empty;
    public IntegrationBundleDocument Bundle { get; set; } = new();
}

/// <summary>声明式集成清单对比入参（支持 YAML 或 JSON 文本）</summary>
public class IntegrationBundleDiffInput
{
    /// <summary>YAML 或 JSON 格式的声明式集成清单原文</summary>
    [Required]
    [MaxLength(524_288)]
    public string Content { get; set; } = string.Empty;
}

/// <summary>单项资源 GitOps 对比 / 执行明细</summary>
public class IntegrationBundleDiffItemDto
{
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Action { get; set; } = "Unchanged";
    public List<string> ChangedFields { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>声明式集成清单只读对比计划</summary>
public class IntegrationBundleDiffResultDto
{
    public int CreateCount { get; set; }
    public int UpdateCount { get; set; }
    public int UnchangedCount { get; set; }
    public int NeedsSecretCount { get; set; }
    public List<IntegrationBundleDiffItemDto> Items { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

/// <summary>声明式集成清单受控应用入参</summary>
public class IntegrationBundleApplyInput
{
    /// <summary>YAML 或 JSON 格式的声明式集成清单原文</summary>
    [Required]
    [MaxLength(524_288)]
    public string Content { get; set; } = string.Empty;

    /// <summary>默认 true（仅输出只读 Dry-Run 计划，不修改任何平台状态）</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>是否允许更新已存在的同名资源（默认 false，仅创建新增资源）</summary>
    public bool AllowUpdateExisting { get; set; }
}

/// <summary>声明式集成清单应用结果</summary>
public class IntegrationBundleApplyResultDto
{
    public bool DryRun { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public List<IntegrationBundleDiffItemDto> Items { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}
