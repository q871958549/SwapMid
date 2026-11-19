using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Volo.Abp;

namespace SwapMid.Controllers.Lineage;

public sealed partial class IntegrationLineageBuilder
{
    public async Task<IntegrationLineageImpactDto> AnalyzeImpactAsync(
        ClaimsPrincipal user,
        string? nodeId,
        string? query,
        string? direction,
        int maxDepth)
    {
        var graph = await BuildGraphAsync(user);
        var root = ResolveRootNode(graph.Nodes, nodeId, query);
        if (root == null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"未在当前血缘图谱中找到匹配的资产节点（nodeId={nodeId ?? "-"}, query={query ?? "-"}）。");
        }

        var clampedDepth = Math.Clamp(maxDepth <= 0 ? 4 : maxDepth, 1, 8);
        var normalizedDir = NormalizeDirection(direction);
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var impacted = new List<IntegrationLineageImpactItemDto>();
        var impactedEdges = new List<IntegrationLineageEdgeDto>();
        var seenEdges = new HashSet<string>(StringComparer.Ordinal);

        void Traverse(string dir)
        {
            var isDown = dir == "downstream";
            var visited = new HashSet<string>(StringComparer.Ordinal) { root.Id };
            var queue = new Queue<(string Id, int Depth, List<string> Path)>();
            queue.Enqueue((root.Id, 0, [root.Name]));

            while (queue.Count > 0)
            {
                var (currId, depth, path) = queue.Dequeue();
                if (depth >= clampedDepth) continue;

                var matchingEdges = isDown
                    ? graph.Edges.Where(e => string.Equals(e.SourceNodeId, currId, StringComparison.Ordinal))
                    : graph.Edges.Where(e => string.Equals(e.TargetNodeId, currId, StringComparison.Ordinal));

                foreach (var edge in matchingEdges)
                {
                    var nextId = isDown ? edge.TargetNodeId : edge.SourceNodeId;
                    if (!nodeMap.TryGetValue(nextId, out var nextNode)) continue;

                    var eKey = $"{edge.SourceNodeId}->{edge.TargetNodeId}:{edge.Relation}";
                    if (seenEdges.Add(eKey))
                    {
                        impactedEdges.Add(edge);
                    }

                    if (!visited.Add(nextId)) continue;

                    var nextPath = new List<string>(path) { nextNode.Name };
                    impacted.Add(new IntegrationLineageImpactItemDto
                    {
                        Node = nextNode,
                        Depth = depth + 1,
                        Direction = dir,
                        PathLabels = nextPath,
                    });
                    queue.Enqueue((nextId, depth + 1, nextPath));
                }
            }
        }

        if (normalizedDir is "downstream" or "both") Traverse("downstream");
        if (normalizedDir is "upstream" or "both") Traverse("upstream");

        var downstreamItems = impacted.Where(i => i.Direction == "downstream").ToList();
        var upstreamItems = impacted.Where(i => i.Direction == "upstream").ToList();
        var hasCriticalDownstream = downstreamItems.Any(i =>
            i.Node.Kind is "LakehouseTable" or "MessageSubscription" or "ApiRoute" ||
            i.Node.Status is "Breached" or "DriftDetected" or "Error");
        var riskLevel = downstreamItems.Count >= 3 || hasCriticalDownstream
            ? "high"
            : (downstreamItems.Count >= 1 ? "medium" : "low");

        var recommendations = BuildRecommendations(root, downstreamItems, riskLevel);
        return new IntegrationLineageImpactDto
        {
            RootNodeId = root.Id,
            RootNodeName = root.Name,
            RootNodeKind = root.Kind,
            Direction = normalizedDir,
            MaxDepth = clampedDepth,
            RiskLevel = riskLevel,
            UpstreamCount = upstreamItems.Count,
            DownstreamCount = downstreamItems.Count,
            ImpactedNodes = impacted,
            ImpactedEdges = impactedEdges,
            Recommendations = recommendations,
            Warnings = graph.Warnings,
            Summary = $"节点「{root.Name}」({root.Kind}) 影响分析完成：上游依赖 {upstreamItems.Count} 项，下游波及 {downstreamItems.Count} 项，风险等级 {riskLevel.ToUpperInvariant()}。",
        };
    }

    private static IntegrationLineageNodeDto? ResolveRootNode(
        IReadOnlyList<IntegrationLineageNodeDto> nodes,
        string? nodeId,
        string? query)
    {
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            var trimmed = nodeId.Trim();
            var exact = nodes.FirstOrDefault(n => string.Equals(n.Id, trimmed, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            if (Guid.TryParse(trimmed, out var guid))
            {
                return nodes.FirstOrDefault(n => n.EntityId == guid);
            }
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            return nodes.FirstOrDefault(n => string.Equals(n.Name, q, StringComparison.OrdinalIgnoreCase))
                ?? nodes.FirstOrDefault(n =>
                    n.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    n.Metadata.Values.Any(v => v.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        return null;
    }

    private static string NormalizeDirection(string? raw)
        => raw?.Trim().ToLowerInvariant() switch
        {
            "downstream" => "downstream",
            "upstream" => "upstream",
            _ => "both",
        };

    private static List<string> BuildRecommendations(
        IntegrationLineageNodeDto root,
        List<IntegrationLineageImpactItemDto> downstream,
        string riskLevel)
    {
        var list = new List<string>();
        if (downstream.Count == 0)
        {
            list.Add("未发现下游消费资产，可直接变更或归档该节点。");
            return list;
        }

        var cdcCount = downstream.Count(i => i.Node.Kind == "CdcStream");
        var lhCount = downstream.Count(i => i.Node.Kind == "LakehouseTable");
        var subCount = downstream.Count(i => i.Node.Kind == "MessageSubscription");
        if (cdcCount > 0)
        {
            list.Add($"下游存在 {cdcCount} 条 CDC 实时流，执行上游 DDL 前建议先核验 SchemaDriftPolicy（推荐 PauseAndAlert）并在必要时切换至 WAP staging 分支隔离写入。");
        }

        if (lhCount > 0)
        {
            list.Add($"变更将波及 {lhCount} 张开放湖仓表，建议先为当前 Iceberg v2 快照创建命名审计标签（lakehouse-tags）作为回滚锚点。");
        }

        if (subCount > 0)
        {
            list.Add($"下游存在 {subCount} 个外部消息订阅消费端，请确认事件字段变更兼容现有 JSONPath 过滤规则与下游反序列化。");
        }

        if (list.Count == 0 && riskLevel != "low")
        {
            list.Add($"请先在测试环境对「{root.Name}」的 {downstream.Count} 项下游依赖执行 Schema 预检后再发布。");
        }

        return list;
    }
}
