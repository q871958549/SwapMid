using System;
using System.Collections.Generic;

namespace SwapMid.Controllers.Lineage;

/// <summary>跨引擎数据血缘拓扑节点（数据源 / 管道 / CDC 流 / 湖仓表 / 消息主题 / 订阅 / API 网关）</summary>
public class IntegrationLineageNodeDto
{
    public string Id { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public Guid? EntityId { get; set; }
    public string Name { get; set; } = null!;
    public string? SubType { get; set; }
    public string Status { get; set; } = "Active";
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>字段级血缘映射条目（源列 -> 目标列及语义转换类型）</summary>
public class IntegrationColumnLineageDto
{
    public string SourceColumn { get; set; } = null!;
    public string TargetColumn { get; set; } = null!;
    public string TransformType { get; set; } = "identity";
}

/// <summary>跨引擎数据血缘有向边</summary>
public class IntegrationLineageEdgeDto
{
    public string SourceNodeId { get; set; } = null!;
    public string TargetNodeId { get; set; } = null!;
    public string Relation { get; set; } = null!;
    public string? Label { get; set; }
    public List<IntegrationColumnLineageDto> ColumnMappings { get; set; } = new();
}

/// <summary>跨引擎数据血缘全量有向无环图（DAG）视图</summary>
public class IntegrationLineageGraphDto
{
    public DateTime GeneratedAtUtc { get; set; }
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public List<IntegrationLineageNodeDto> Nodes { get; set; } = new();
    public List<IntegrationLineageEdgeDto> Edges { get; set; } = new();
    public Dictionary<string, int> CountsByKind { get; set; } = new(StringComparer.Ordinal);
    public List<string> Warnings { get; set; } = new();
    public string Summary { get; set; } = null!;
}

/// <summary>影响分析中的受波及节点条目</summary>
public class IntegrationLineageImpactItemDto
{
    public IntegrationLineageNodeDto Node { get; set; } = null!;
    public int Depth { get; set; }
    public string Direction { get; set; } = "downstream";
    public List<string> PathLabels { get; set; } = new();
}

/// <summary>跨引擎变更影响面分析（Blast Radius）结果</summary>
public class IntegrationLineageImpactDto
{
    public string RootNodeId { get; set; } = null!;
    public string RootNodeName { get; set; } = null!;
    public string RootNodeKind { get; set; } = null!;
    public string Direction { get; set; } = "both";
    public int MaxDepth { get; set; }
    public string RiskLevel { get; set; } = "low";
    public int UpstreamCount { get; set; }
    public int DownstreamCount { get; set; }
    public List<IntegrationLineageImpactItemDto> ImpactedNodes { get; set; } = new();
    public List<IntegrationLineageEdgeDto> ImpactedEdges { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Summary { get; set; } = null!;
}

/// <summary>CNCF OpenLineage 2.0（v2-0-2）标准血缘导出结果（兼容 Marquez / DataHub / OpenMetadata）</summary>
public class OpenLineageExportDto
{
    public string SpecVersion { get; set; } = "2-0-2";
    public string Producer { get; set; } = null!;
    public string SchemaUrl { get; set; } = null!;
    public string Namespace { get; set; } = "swapmid";
    public DateTime GeneratedAtUtc { get; set; }
    public int EventCount { get; set; }
    public int DatasetCount { get; set; }
    public int ColumnLineageEdgeCount { get; set; }
    public string ContentJson { get; set; } = null!;
    public string Summary { get; set; } = null!;
}
