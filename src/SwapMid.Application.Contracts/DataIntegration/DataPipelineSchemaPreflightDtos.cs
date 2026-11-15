using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.DataIntegration;

/// <summary>数据源连接器能力声明。</summary>
public sealed class ConnectorCapabilityDto
{
    /// <summary>标准化连接器标识（postgresql / sqlserver / mysql / rest-api / file）。</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>连接器显示名称。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>所属数据源大类。</summary>
    public DataSourceType DataSourceType { get; init; }

    /// <summary>是否支持作为流式 ETL 源读取。</summary>
    public bool SupportsSourceRead { get; init; }

    /// <summary>是否支持作为关系型目标写入。</summary>
    public bool SupportsTargetWrite { get; init; }

    /// <summary>是否支持原子增量合并（Upsert）。</summary>
    public bool SupportsAtomicUpsert { get; init; }

    /// <summary>是否支持目标库机器回执 v1（__swapmid_etl_receipt_v1）。</summary>
    public bool SupportsTargetReceiptV1 { get; init; }

    /// <summary>机器回执模式是否强制要求严格 TLS（encrypt=true 且 trustServerCertificate=false）。</summary>
    public bool RequiresStrictTlsForReceipt { get; init; }

    /// <summary>支持的写入模式列表。</summary>
    public IReadOnlyList<string> SupportedWriteModes { get; init; } = Array.Empty<string>();
}

/// <summary>数据管道源查询与目标表结构静态预检请求。</summary>
public sealed class PreflightPipelineSchemaInput
{
    /// <summary>源数据源 ID。</summary>
    [Required]
    public Guid SourceId { get; set; }

    /// <summary>目标数据源 ID（可选）。</summary>
    public Guid? TargetId { get; set; }

    /// <summary>ETL 转换规则 JSON（含 sourceQuery / targetTable / mode / keyColumns / watermarkColumn）。</summary>
    [Required]
    [MaxLength(131072)]
    public string TransformConfig { get; set; } = string.Empty;
}

/// <summary>预检列元数据。</summary>
public sealed class PreflightColumnDto
{
    /// <summary>列名。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>数据库类型或 CLR 类型描述。</summary>
    public string DataType { get; init; } = string.Empty;

    /// <summary>是否允许空值。</summary>
    public bool IsNullable { get; init; }

    /// <summary>是否为主键列。</summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>是否受非过滤唯一索引或主键覆盖。</summary>
    public bool IsUniqueIndexed { get; init; }

    /// <summary>目标列是否定义了默认值或自增计算。</summary>
    public bool HasDefaultOrIdentity { get; init; }
}

/// <summary>结构兼容性预检诊断项。</summary>
public sealed class PreflightSchemaIssueDto
{
    /// <summary>严重级别：error 或 warning。</summary>
    public string Severity { get; init; } = "error";

    /// <summary>稳定诊断代码。</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>面向操作员的诊断说明（不含敏感连接串或驱动原文）。</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>数据管道 Schema 兼容性预检结果。</summary>
public sealed class PipelineSchemaPreflightResultDto
{
    /// <summary>是否存在零阻断错误（severity=error 为 0 时为 true）。</summary>
    public bool Compatible { get; set; }

    /// <summary>源连接器标识。</summary>
    public string SourceProvider { get; set; } = string.Empty;

    /// <summary>目标连接器标识（未选目标源时为 null）。</summary>
    public string? TargetProvider { get; set; }

    /// <summary>解析后的写入模式。</summary>
    public string WriteMode { get; set; } = string.Empty;

    /// <summary>源查询零行 Schema 探测列清单。</summary>
    public List<PreflightColumnDto> SourceColumns { get; set; } = new();

    /// <summary>目标表结构列清单。</summary>
    public List<PreflightColumnDto> TargetColumns { get; set; } = new();

    /// <summary>兼容性诊断问题列表。</summary>
    public List<PreflightSchemaIssueDto> Issues { get; set; } = new();
}
