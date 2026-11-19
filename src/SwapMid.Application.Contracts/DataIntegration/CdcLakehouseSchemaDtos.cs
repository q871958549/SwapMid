using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.DataIntegration;

/// <summary>Apache Iceberg v2 活跃表结构字段信息（含不可变 FieldId）</summary>
public class CdcLakehouseSchemaFieldDto
{
    public int FieldId { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public bool IsCoreEnvelope { get; set; }
    public string? Doc { get; set; }
}

/// <summary>Apache Iceberg v2 Schema Evolution 新增类型化业务列定义</summary>
public class AddCdcLakehouseColumnInput
{
    /// <summary>列名（字母或下划线开头，1-64 字符，匹配上游 payload 字段自动提升）</summary>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Name { get; set; } = null!;

    /// <summary>Iceberg 原始类型：string | long | double | boolean | timestamptz</summary>
    [Required]
    [StringLength(32, MinimumLength = 1)]
    public string Type { get; set; } = "string";

    /// <summary>可选列注释说明</summary>
    [StringLength(256)]
    public string? Doc { get; set; }
}

/// <summary>Apache Iceberg v2 Schema Evolution 零重写列重命名（保留原 FieldId）</summary>
public class RenameCdcLakehouseColumnInput
{
    /// <summary>当前列名（不可为 1..12 核心信封列）</summary>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string FromName { get; set; } = null!;

    /// <summary>目标新列名（保留完全一致的 FieldId，历史 Parquet 文件免重写可读）</summary>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string ToName { get; set; } = null!;
}

/// <summary>Apache Iceberg v2 原生 Schema Evolution 入参（支持增列、保留 FieldId 重命名与永不复用 FieldId 删除）</summary>
public class EvolveCdcLakehouseSchemaInput
{
    /// <summary>待新增并提升为 Parquet 一等列的类型化业务列列表</summary>
    public List<AddCdcLakehouseColumnInput> AddColumns { get; set; } = new();

    /// <summary>待重命名的自定义列列表（保留原 FieldId 并自动扩展 schema.name-mapping.default）</summary>
    public List<RenameCdcLakehouseColumnInput> RenameColumns { get; set; } = new();

    /// <summary>待从活跃 Schema 移除的自定义列名（受保护的 1..12 核心信封列不可删除，已删 FieldId 永不复用）</summary>
    public List<string> DropColumns { get; set; } = new();

    /// <summary>默认 true（仅执行演进预演与兼容性校验，绝不修改表元数据）</summary>
    public bool DryRun { get; set; } = true;
}

/// <summary>Apache Iceberg v2 原生 Schema Evolution 预演 / 执行结果</summary>
public class CdcLakehouseSchemaEvolutionResultDto
{
    public Guid StreamId { get; set; }
    public bool DryRun { get; set; }
    public int PreviousSchemaId { get; set; }
    public int NewSchemaId { get; set; }
    public int LastColumnId { get; set; }
    public long MetadataVersion { get; set; }
    public string MetadataKey { get; set; } = null!;
    public int AddedCount { get; set; }
    public int RenamedCount { get; set; }
    public int DroppedCount { get; set; }
    public List<CdcLakehouseSchemaFieldDto> ActiveFields { get; set; } = new();
    public DateTime EvolvedAtUtc { get; set; }
    public string Message { get; set; } = null!;
}
