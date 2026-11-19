using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SwapMid.Controllers.Lineage;

namespace SwapMid.Mcp;

/// <summary>
/// MCP Tool 14（CNCF OpenLineage 2.0 标准血缘导出）：
/// 只读生成当前用户 RBAC 权限范围内的 OpenLineage <c>v2-0-2</c> 规范文档（含字段级 <c>columnLineage</c> 与 <c>schema</c> facets），
/// 可直接对接 Marquez / DataHub / OpenMetadata（严格零密钥）。
/// </summary>
internal static class McpOpenLineageHandler
{
    internal static async Task<object> ExecuteAsync(
        IServiceProvider serviceProvider,
        ClaimsPrincipal user,
        JsonElement args)
    {
        var builder = serviceProvider.GetRequiredService<IntegrationLineageBuilder>();
        var ns = McpJsonRpcHelpers.ReadOptionalString(args, "namespace");
        var summaryOnly = McpJsonRpcHelpers.ReadOptionalBool(args, "summaryOnly");

        var export = await builder.BuildOpenLineageExportAsync(user, ns);
        if (summaryOnly)
        {
            return new
            {
                export.SpecVersion,
                export.Producer,
                export.SchemaUrl,
                export.Namespace,
                export.GeneratedAtUtc,
                export.EventCount,
                export.DatasetCount,
                export.ColumnLineageEdgeCount,
                export.Summary,
            };
        }

        return export;
    }
}
