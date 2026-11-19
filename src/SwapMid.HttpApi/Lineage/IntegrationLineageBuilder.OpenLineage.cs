using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using SwapMid.DataIntegration;

namespace SwapMid.Controllers.Lineage;

public sealed partial class IntegrationLineageBuilder
{
    public const string OpenLineageSpecVersion = "2-0-2";
    public const string OpenLineageProducer = "https://github.com/q871958549/SwapMid";
    public const string OpenLineageSchemaUrl = "https://openlineage.io/spec/2-0-2/OpenLineage.json";
    private const string ColumnLineageFacetSchemaUrl = "https://openlineage.io/spec/facets/1-2-0/ColumnLineageDatasetFacet.json";
    private const string SchemaFacetSchemaUrl = "https://openlineage.io/spec/facets/1-1-1/SchemaDatasetFacet.json";

    private static readonly JsonSerializerOptions OpenLineageJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// 将当前 RBAC 可见的跨引擎血缘拓扑与字段级映射导出为标准 CNCF OpenLineage 2.0（v2-0-2）JSON 规范文档，
    /// 可直接导入 Marquez / DataHub / OpenMetadata 等企业级元数据目录（严格零密钥）。
    /// </summary>
    public async Task<OpenLineageExportDto> BuildOpenLineageExportAsync(ClaimsPrincipal user, string? @namespace = null)
    {
        var graph = await BuildGraphAsync(user);
        var ns = string.IsNullOrWhiteSpace(@namespace) ? "swapmid" : @namespace.Trim();
        var nodeById = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var incoming = graph.Edges.GroupBy(e => e.TargetNodeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var outgoing = graph.Edges.GroupBy(e => e.SourceNodeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var jobNodes = graph.Nodes
            .Where(n => n.Kind is "DataPipeline" or "CdcStream")
            .OrderBy(n => n.Kind, StringComparer.Ordinal)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();

        var events = new List<object>(jobNodes.Count);
        var distinctDatasets = new HashSet<string>(StringComparer.Ordinal);
        var columnLineageEdges = 0;
        var eventTime = graph.GeneratedAtUtc.ToString("O");

        foreach (var jobNode in jobNodes)
        {
            var inEdges = incoming.TryGetValue(jobNode.Id, out var inList) ? inList : new List<IntegrationLineageEdgeDto>();
            var outEdges = outgoing.TryGetValue(jobNode.Id, out var outList) ? outList : new List<IntegrationLineageEdgeDto>();

            var inputs = new List<object>();
            var primaryInputDsNamespace = $"{ns}:source";
            var primaryInputDsName = jobNode.Name;

            foreach (var edge in inEdges)
            {
                if (!nodeById.TryGetValue(edge.SourceNodeId, out var srcNode)) continue;
                var dsNs = $"{ns}:{srcNode.Kind.ToLowerInvariant()}";
                var dsName = string.IsNullOrWhiteSpace(edge.Label)
                    ? srcNode.Name
                    : $"{srcNode.Name}/{edge.Label}";
                distinctDatasets.Add($"{dsNs}::{dsName}");
                if (inputs.Count == 0)
                {
                    primaryInputDsNamespace = dsNs;
                    primaryInputDsName = dsName;
                }

                inputs.Add(new
                {
                    @namespace = dsNs,
                    name = dsName,
                    facets = new Dictionary<string, object>
                    {
                        ["swapmidNode"] = new
                        {
                            _producer = OpenLineageProducer,
                            _schemaURL = OpenLineageSchemaUrl,
                            nodeId = srcNode.Id,
                            kind = srcNode.Kind,
                            subType = srcNode.SubType,
                            status = srcNode.Status,
                        },
                    },
                });
            }

            var outputs = new List<object>();
            foreach (var edge in outEdges)
            {
                if (!nodeById.TryGetValue(edge.TargetNodeId, out var tgtNode)) continue;
                var dsNs = $"{ns}:{tgtNode.Kind.ToLowerInvariant()}";
                var dsName = tgtNode.Kind == "LakehouseTable" || string.IsNullOrWhiteSpace(edge.Label)
                    ? tgtNode.Name
                    : $"{tgtNode.Name}/{edge.Label}";
                distinctDatasets.Add($"{dsNs}::{dsName}");

                var facets = new Dictionary<string, object>
                {
                    ["swapmidNode"] = new
                    {
                        _producer = OpenLineageProducer,
                        _schemaURL = OpenLineageSchemaUrl,
                        nodeId = tgtNode.Id,
                        kind = tgtNode.Kind,
                        subType = tgtNode.SubType,
                        status = tgtNode.Status,
                        relation = edge.Relation,
                    },
                };

                if (edge.ColumnMappings.Count > 0)
                {
                    columnLineageEdges++;
                    var schemaFields = edge.ColumnMappings
                        .Select(m => m.TargetColumn)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(col => new { name = col, type = "STRING" })
                        .ToList();
                    facets["schema"] = new
                    {
                        _producer = OpenLineageProducer,
                        _schemaURL = SchemaFacetSchemaUrl,
                        fields = schemaFields,
                    };

                    var colFields = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var grp in edge.ColumnMappings.GroupBy(m => m.TargetColumn, StringComparer.OrdinalIgnoreCase))
                    {
                        colFields[grp.Key] = new
                        {
                            inputFields = grp.Select(m => new
                            {
                                @namespace = primaryInputDsNamespace,
                                name = primaryInputDsName,
                                field = m.SourceColumn,
                                transformations = new[]
                                {
                                    new
                                    {
                                        type = MapOpenLineageTransformCategory(m.TransformType),
                                        subtype = m.TransformType.ToUpperInvariant(),
                                        description = $"SwapMid {m.TransformType} mapping",
                                        masking = false,
                                    },
                                },
                            }).ToList(),
                        };
                    }

                    facets["columnLineage"] = new
                    {
                        _producer = OpenLineageProducer,
                        _schemaURL = ColumnLineageFacetSchemaUrl,
                        fields = colFields,
                    };
                }

                outputs.Add(new
                {
                    @namespace = dsNs,
                    name = dsName,
                    facets,
                });
            }

            events.Add(new
            {
                eventType = "COMPLETE",
                eventTime,
                producer = OpenLineageProducer,
                schemaURL = $"{OpenLineageSchemaUrl}#/$defs/RunEvent",
                run = new { runId = (jobNode.EntityId ?? Guid.NewGuid()).ToString("D") },
                job = new
                {
                    @namespace = ns,
                    name = $"{jobNode.Kind.ToLowerInvariant()}.{jobNode.Name}",
                    facets = new
                    {
                        swapmidMetadata = new
                        {
                            _producer = OpenLineageProducer,
                            _schemaURL = OpenLineageSchemaUrl,
                            nodeId = jobNode.Id,
                            subType = jobNode.SubType,
                            status = jobNode.Status,
                            metadata = jobNode.Metadata,
                        },
                    },
                },
                inputs,
                outputs,
            });
        }

        var specDocument = new
        {
            specVersion = OpenLineageSpecVersion,
            producer = OpenLineageProducer,
            schemaURL = OpenLineageSchemaUrl,
            @namespace = ns,
            generatedAt = eventTime,
            nodeCount = graph.NodeCount,
            edgeCount = graph.EdgeCount,
            eventCount = events.Count,
            datasetCount = distinctDatasets.Count,
            columnLineageEdgeCount = columnLineageEdges,
            events,
        };

        var json = JsonSerializer.Serialize(specDocument, OpenLineageJsonOptions);
        return new OpenLineageExportDto
        {
            SpecVersion = OpenLineageSpecVersion,
            Producer = OpenLineageProducer,
            SchemaUrl = OpenLineageSchemaUrl,
            Namespace = ns,
            GeneratedAtUtc = graph.GeneratedAtUtc,
            EventCount = events.Count,
            DatasetCount = distinctDatasets.Count,
            ColumnLineageEdgeCount = columnLineageEdges,
            ContentJson = json,
            Summary = $"已导出 CNCF OpenLineage {OpenLineageSpecVersion} 规范血缘：{events.Count} 个作业事件、{distinctDatasets.Count} 个数据集、{columnLineageEdges} 条字段级血缘边。",
        };
    }

    private static string MapOpenLineageTransformCategory(string transformType) => transformType switch
    {
        "identity" or "business_key" or "watermark" => "DIRECT",
        "rename" => "DIRECT",
        "soft_delete" => "INDIRECT",
        _ => "DIRECT",
    };

    private static List<IntegrationColumnLineageDto> ExtractCdcColumnLineage(CdcStreamDto cdc)
    {
        var list = new List<IntegrationColumnLineageDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCol(string src, string tgt, string type)
        {
            var s = src.Trim();
            var t = tgt.Trim();
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(t)) return;
            if (seen.Add($"{s}->{t}:{type}"))
            {
                list.Add(new IntegrationColumnLineageDto
                {
                    SourceColumn = s,
                    TargetColumn = t,
                    TransformType = type,
                });
            }
        }

        if (cdc.TargetFieldMappings is { Count: > 0 })
        {
            foreach (var map in cdc.TargetFieldMappings)
            {
                if (string.IsNullOrWhiteSpace(map.SourceColumn) || string.IsNullOrWhiteSpace(map.TargetColumn)) continue;
                var transform = string.Equals(map.SourceColumn.Trim(), map.TargetColumn.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? "identity"
                    : "rename";
                AddCol(map.SourceColumn, map.TargetColumn, transform);
            }
        }

        if (!string.IsNullOrWhiteSpace(cdc.TargetSinkKeyColumns))
        {
            foreach (var key in cdc.TargetSinkKeyColumns.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddCol(key, key, "business_key");
            }
        }

        if (!string.IsNullOrWhiteSpace(cdc.SoftDeleteColumn))
        {
            AddCol("__cdc_op", cdc.SoftDeleteColumn, "soft_delete");
        }

        if (!string.IsNullOrWhiteSpace(cdc.SoftDeleteTimestampColumn))
        {
            AddCol("__cdc_commit_ts", cdc.SoftDeleteTimestampColumn, "soft_delete");
        }

        return list;
    }
}
