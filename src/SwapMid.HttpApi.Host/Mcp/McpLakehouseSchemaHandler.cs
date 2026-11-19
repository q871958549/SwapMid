using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SwapMid.DataIntegration;

namespace SwapMid.Mcp;

/// <summary>
/// MCP Tool 15（Apache Iceberg v2 表结构与 Schema Evolution 只读检视）：
/// 返回指定或全部 Apache Iceberg v2 流的当前 <c>schema-id</c>、<c>last-column-id</c>、核心信封列与已提升的业务列清单，
/// 辅助 AI Agent 安全规划增列 / 零重写重命名 / 删列演进方案。
/// </summary>
internal static class McpLakehouseSchemaHandler
{
    internal static async Task<object> ExecuteAsync(
        IServiceProvider serviceProvider,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var streamService = serviceProvider.GetRequiredService<ICdcStreamAppService>();
        var streamId = McpJsonRpcHelpers.ReadOptionalGuid(args, "streamId");

        var targets = new List<CdcStreamDto>();
        if (streamId.HasValue)
        {
            targets.Add(await streamService.GetAsync(streamId.Value));
        }
        else
        {
            var page = await streamService.GetListAsync(new GetCdcStreamListInput { MaxResultCount = 50, SkipCount = 0 });
            targets.AddRange(page.Items
                .Where(s => s.TargetMode == CdcTargetMode.ExternalSink &&
                            s.TargetSinkType == DataSourceType.ObjectStorage &&
                            s.LakehouseTableFormat == LakehouseTableFormat.ApacheIceberg)
                .Take(10));
        }

        if (targets.Count == 0)
        {
            return new
            {
                icebergStreamCount = 0,
                schemas = Array.Empty<object>(),
                message = "No Apache Iceberg v2 CDC streams configured.",
            };
        }

        var schemas = new List<object>(targets.Count);
        foreach (var stream in targets)
        {
            var history = await streamService.GetLakehouseHistoryAsync(
                stream.Id,
                new GetCdcLakehouseHistoryInput { Version = null, MaxCommits = 5 },
                cancellationToken);

            var promotedFields = history.SchemaFields.Where(f => !f.IsCoreEnvelope).ToList();
            schemas.Add(new
            {
                streamId = stream.Id,
                streamName = stream.Name,
                destination = history.Destination,
                currentVersion = history.CurrentVersion,
                currentSchemaId = history.CurrentSchemaId,
                lastColumnId = history.LastColumnId,
                coreEnvelopeFieldCount = history.SchemaFields.Count(f => f.IsCoreEnvelope),
                promotedFieldCount = promotedFields.Count,
                fields = history.SchemaFields.Select(f => new
                {
                    f.FieldId,
                    f.Name,
                    f.Type,
                    f.Required,
                    f.IsCoreEnvelope,
                    f.Doc,
                }),
                supportedTypes = new[] { "string", "long", "double", "boolean", "timestamptz" },
                evolutionEndpoint = $"/api/v1/cdc-streams/{stream.Id}/evolve-lakehouse-schema",
            });
        }

        return new
        {
            icebergStreamCount = schemas.Count,
            schemas,
        };
    }
}
