using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SwapMid.Controllers.GitOps;

namespace SwapMid.Mcp;

/// <summary>
/// MCP Tool 11（只读 GitOps 拓扑导出）：以零明文密钥格式（YAML / JSON，swapmid.io/v1）导出平台当前全部数据源、管道、CDC 流、消息主题/订阅与 API 路由声明。
/// </summary>
internal static class McpIntegrationBundleHandler
{
    internal static async Task<object> ExecuteAsync(
        IServiceProvider serviceProvider,
        ClaimsPrincipal user,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var orchestrator = serviceProvider.GetRequiredService<IntegrationBundleOrchestrator>();
        var format = McpJsonRpcHelpers.ReadOptionalString(args, "format") ?? "yaml";
        var includeContent = !(args.ValueKind == JsonValueKind.Object &&
                               args.TryGetProperty("summaryOnly", out var sumElem) &&
                               sumElem.ValueKind == JsonValueKind.True);

        var exported = await orchestrator.ExportAsync(user, format);
        return new
        {
            exported.SchemaVersion,
            exported.Format,
            exported.ResourceCount,
            counts = new
            {
                dataSources = exported.Bundle.DataSources.Count,
                dataPipelines = exported.Bundle.DataPipelines.Count,
                cdcStreams = exported.Bundle.CdcStreams.Count,
                messageTopics = exported.Bundle.MessageTopics.Count,
                messageSubscriptions = exported.Bundle.MessageSubscriptions.Count,
                apiGroups = exported.Bundle.ApiGroups.Count,
                apiRoutes = exported.Bundle.ApiRoutes.Count,
            },
            exported.Warnings,
            note = "Secret-free GitOps manifest (swapmid.io/v1). Use POST /api/v1/integration-bundles/diff or swapmidctl to plan and apply across environments.",
            content = includeContent ? exported.Content : null,
        };
    }
}
