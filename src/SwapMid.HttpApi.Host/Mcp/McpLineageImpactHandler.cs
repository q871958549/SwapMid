using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SwapMid.Controllers.Lineage;

namespace SwapMid.Mcp;

/// <summary>
/// MCP Tool 13（跨引擎数据血缘与变更影响分析）：
/// 只读查询 SwapMid 全域血缘图谱，或在变更上游数据源/表/管道/主题前计算上下游影响面与风险等级。
/// </summary>
internal static class McpLineageImpactHandler
{
    internal static async Task<object> ExecuteAsync(
        IServiceProvider serviceProvider,
        ClaimsPrincipal user,
        JsonElement args)
    {
        var builder = serviceProvider.GetRequiredService<IntegrationLineageBuilder>();
        var nodeId = McpJsonRpcHelpers.ReadOptionalString(args, "nodeId");
        var query = McpJsonRpcHelpers.ReadOptionalString(args, "query");
        var direction = McpJsonRpcHelpers.ReadOptionalString(args, "direction") ?? "both";
        var maxDepth = McpJsonRpcHelpers.ReadClampedInt(args, "maxDepth", defaultValue: 4, min: 1, max: 8);

        if (string.IsNullOrWhiteSpace(nodeId) && string.IsNullOrWhiteSpace(query))
        {
            var graph = await builder.BuildGraphAsync(user);
            return new
            {
                mode = "topology",
                graph.GeneratedAtUtc,
                graph.NodeCount,
                graph.EdgeCount,
                graph.CountsByKind,
                nodes = graph.Nodes.Take(50).Select(n => new { n.Id, n.Kind, n.Name, n.SubType, n.Status }),
                edges = graph.Edges.Take(100),
                graph.Warnings,
                graph.Summary,
            };
        }

        var impact = await builder.AnalyzeImpactAsync(user, nodeId, query, direction, maxDepth);
        return new
        {
            mode = "impact",
            impact.RootNodeId,
            impact.RootNodeName,
            impact.RootNodeKind,
            impact.Direction,
            impact.MaxDepth,
            impact.RiskLevel,
            impact.UpstreamCount,
            impact.DownstreamCount,
            impactedNodes = impact.ImpactedNodes.Select(i => new
            {
                i.Node.Id,
                i.Node.Kind,
                i.Node.Name,
                i.Node.SubType,
                i.Node.Status,
                i.Depth,
                i.Direction,
                i.PathLabels,
            }),
            impact.Recommendations,
            impact.Warnings,
            impact.Summary,
        };
    }
}
