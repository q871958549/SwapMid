using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using SwapMid.ApiManagement;
using SwapMid.DataIntegration;
using SwapMid.MessageBus;
using SwapMid.Permissions;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.Controllers.Lineage;

/// <summary>
/// 跨引擎实时数据血缘图谱与影响面分析引擎（DataSources -> ETL Pipelines / CDC Streams -> Lakehouse Tables / MessageBus Topics -> Subscriptions &amp; API Gateway）：
/// 严格只读、零明文凭据、按域 RBAC 权限聚合，绝不破坏 <c>SwapMid.Application</c> 域间零耦合基线。
/// </summary>
public sealed partial class IntegrationLineageBuilder : ITransientDependency
{
    private const string PipelineEventsTopicName = "integration.pipeline.events";

    private readonly IDataSourceAppService _dataSourceAppService;
    private readonly IDataPipelineAppService _dataPipelineAppService;
    private readonly ICdcStreamAppService _cdcStreamAppService;
    private readonly IMessageTopicAppService _messageTopicAppService;
    private readonly IMessageSubscriptionAppService _messageSubscriptionAppService;
    private readonly IApiGroupAppService _apiGroupAppService;
    private readonly IApiRouteAppService _apiRouteAppService;
    private readonly IAuthorizationService _authorizationService;

    public IntegrationLineageBuilder(
        IDataSourceAppService dataSourceAppService,
        IDataPipelineAppService dataPipelineAppService,
        ICdcStreamAppService cdcStreamAppService,
        IMessageTopicAppService messageTopicAppService,
        IMessageSubscriptionAppService messageSubscriptionAppService,
        IApiGroupAppService apiGroupAppService,
        IApiRouteAppService apiRouteAppService,
        IAuthorizationService authorizationService)
    {
        _dataSourceAppService = dataSourceAppService;
        _dataPipelineAppService = dataPipelineAppService;
        _cdcStreamAppService = cdcStreamAppService;
        _messageTopicAppService = messageTopicAppService;
        _messageSubscriptionAppService = messageSubscriptionAppService;
        _apiGroupAppService = apiGroupAppService;
        _apiRouteAppService = apiRouteAppService;
        _authorizationService = authorizationService;
    }

    public async Task<IntegrationLineageGraphDto> BuildGraphAsync(ClaimsPrincipal user)
    {
        var nodes = new Dictionary<string, IntegrationLineageNodeDto>(StringComparer.Ordinal);
        var edges = new List<IntegrationLineageEdgeDto>();
        var edgeSet = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();

        void AddNode(IntegrationLineageNodeDto node) => nodes.TryAdd(node.Id, node);
        void AddEdge(
            string sourceId,
            string targetId,
            string relation,
            string? label = null,
            List<IntegrationColumnLineageDto>? columnMappings = null)
        {
            if (string.Equals(sourceId, targetId, StringComparison.Ordinal)) return;
            var key = $"{sourceId}->{targetId}:{relation}:{label ?? string.Empty}";
            if (edgeSet.Add(key))
            {
                edges.Add(new IntegrationLineageEdgeDto
                {
                    SourceNodeId = sourceId,
                    TargetNodeId = targetId,
                    Relation = relation,
                    Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
                    ColumnMappings = columnMappings ?? new List<IntegrationColumnLineageDto>(),
                });
            }
        }

        if (await HasPermissionAsync(user, SwapMidPermissions.DataIntegration.DataSources.Default))
        {
            var page = await _dataSourceAppService.GetListAsync(new GetDataSourceListInput { MaxResultCount = 500 });
            foreach (var ds in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                AddNode(new IntegrationLineageNodeDto
                {
                    Id = $"source:{ds.Id:D}",
                    Kind = "DataSource",
                    EntityId = ds.Id,
                    Name = ds.Name,
                    SubType = ds.Type.ToString(),
                    Status = ds.IsEnabled ? "Active" : "Disabled",
                });
            }
        }
        else warnings.Add("缺少 DataSources 读取权限，数据源节点已省略。");

        var topicNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (await HasPermissionAsync(user, SwapMidPermissions.MessageBus.Topics.Default))
        {
            var page = await _messageTopicAppService.GetListAsync(new GetMessageTopicListInput { MaxResultCount = 500 });
            foreach (var topic in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                topicNames.Add(topic.Name);
                AddNode(new IntegrationLineageNodeDto
                {
                    Id = $"topic:{topic.Name}",
                    Kind = "MessageTopic",
                    EntityId = topic.Id,
                    Name = topic.Name,
                    SubType = topic.Type.ToString(),
                    Status = topic.IsEnabled ? "Active" : "Disabled",
                });
            }
        }

        if (await HasPermissionAsync(user, SwapMidPermissions.DataIntegration.DataPipelines.Default))
        {
            var page = await _dataPipelineAppService.GetListAsync(new GetDataPipelineListInput { MaxResultCount = 500 });
            foreach (var pipe in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var (mode, targetTable, pipelineCols) = ExtractTransformHints(pipe.TransformConfig);
                var pipeId = $"pipeline:{pipe.Id:D}";
                var meta = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!string.IsNullOrWhiteSpace(mode)) meta["mode"] = mode;
                if (!string.IsNullOrWhiteSpace(targetTable)) meta["targetTable"] = targetTable;
                if (pipe.ScheduleEnabled && !string.IsNullOrWhiteSpace(pipe.ScheduleCron)) meta["cron"] = pipe.ScheduleCron;

                AddNode(new IntegrationLineageNodeDto
                {
                    Id = pipeId,
                    Kind = "DataPipeline",
                    EntityId = pipe.Id,
                    Name = pipe.Name,
                    SubType = string.IsNullOrWhiteSpace(mode) ? "ETL" : $"ETL ({mode})",
                    Status = pipe.Status.ToString(),
                    Metadata = meta,
                });

                var srcKey = $"source:{pipe.SourceId:D}";
                if (nodes.ContainsKey(srcKey))
                {
                    AddEdge(srcKey, pipeId, "ReadsFrom", "SELECT");
                }

                if (pipe.TargetId.HasValue)
                {
                    var tgtKey = $"source:{pipe.TargetId.Value:D}";
                    if (nodes.ContainsKey(tgtKey))
                    {
                        AddEdge(pipeId, tgtKey, "WritesTo", targetTable, pipelineCols);
                    }
                }

                if (topicNames.Contains(PipelineEventsTopicName))
                {
                    AddEdge(pipeId, $"topic:{PipelineEventsTopicName}", "PublishesTo", "Outbox");
                }
            }
        }

        if (await HasPermissionAsync(user, SwapMidPermissions.DataIntegration.CdcStreams.Default))
        {
            var page = await _cdcStreamAppService.GetListAsync(new GetCdcStreamListInput { MaxResultCount = 500 });
            foreach (var cdc in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var cdcId = $"cdc:{cdc.Id:D}";
                var cdcStatus = cdc.ContractViolated
                    ? "Breached"
                    : (cdc.SchemaDriftDetected ? "DriftDetected" : cdc.Status.ToString());
                var meta = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sourceType"] = cdc.SourceType.ToString(),
                    ["targetMode"] = cdc.TargetMode.ToString(),
                };
                if (!string.IsNullOrWhiteSpace(cdc.MonitoredTables)) meta["monitoredTables"] = cdc.MonitoredTables;
                if (!string.IsNullOrWhiteSpace(cdc.TargetSinkDestination)) meta["destination"] = cdc.TargetSinkDestination;
                if (!string.IsNullOrWhiteSpace(cdc.LakehouseWriteBranch)) meta["writeBranch"] = cdc.LakehouseWriteBranch;
                if (!string.IsNullOrWhiteSpace(cdc.LakehouseMaintenanceCron)) meta["maintenanceCron"] = cdc.LakehouseMaintenanceCron;
                if (cdc.SchemaDriftDetected) meta["schemaDrift"] = "true";
                if (cdc.ContractViolated) meta["contractStatus"] = "Breached";

                AddNode(new IntegrationLineageNodeDto
                {
                    Id = cdcId,
                    Kind = "CdcStream",
                    EntityId = cdc.Id,
                    Name = cdc.Name,
                    SubType = cdc.SourceType.ToString(),
                    Status = cdcStatus,
                    Metadata = meta,
                });

                var srcKey = $"source:{cdc.SourceId:D}";
                if (nodes.ContainsKey(srcKey))
                {
                    AddEdge(srcKey, cdcId, "CapturesFrom", cdc.MonitoredTables ?? cdc.PublicationName);
                }

                if (cdc.TargetMode == CdcTargetMode.MessageBusTopic && !string.IsNullOrWhiteSpace(cdc.TargetTopicName))
                {
                    var topicId = $"topic:{cdc.TargetTopicName}";
                    AddNode(new IntegrationLineageNodeDto
                    {
                        Id = topicId,
                        Kind = "MessageTopic",
                        Name = cdc.TargetTopicName,
                        SubType = "Event",
                        Status = "Active",
                    });
                    AddEdge(cdcId, topicId, "PublishesTo", "CloudEvents");
                }
                else if (cdc.TargetMode == CdcTargetMode.DataPipeline && cdc.TargetPipelineId.HasValue)
                {
                    var pipeId = $"pipeline:{cdc.TargetPipelineId.Value:D}";
                    if (nodes.ContainsKey(pipeId))
                    {
                        AddEdge(cdcId, pipeId, "TriggersPipeline", "CDC Trigger");
                    }
                }
                else if (cdc.TargetMode == CdcTargetMode.ExternalSink && cdc.TargetSinkId.HasValue)
                {
                    var sinkKey = $"source:{cdc.TargetSinkId.Value:D}";
                    if (cdc.TargetSinkType == DataSourceType.ObjectStorage && !string.IsNullOrWhiteSpace(cdc.TargetSinkDestination))
                    {
                        var lhId = $"lakehouse:{cdc.TargetSinkId.Value:D}:{cdc.TargetSinkDestination.Trim()}";
                        var branchTag = !string.IsNullOrWhiteSpace(cdc.LakehouseWriteBranch) ? $" [{cdc.LakehouseWriteBranch}]" : string.Empty;
                        var morTag = cdc.LakehouseWriteMode == LakehouseWriteMode.MergeOnReadUpsert ? " MoR" : string.Empty;
                        var lhMeta = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["tableFormat"] = cdc.LakehouseTableFormat.ToString(),
                            ["writeMode"] = cdc.LakehouseWriteMode.ToString(),
                            ["destination"] = cdc.TargetSinkDestination.Trim(),
                        };
                        if (!string.IsNullOrWhiteSpace(cdc.LakehouseWriteBranch)) lhMeta["writeBranch"] = cdc.LakehouseWriteBranch;
                        if (!string.IsNullOrWhiteSpace(cdc.LakehouseMaintenanceCron)) lhMeta["maintenanceCron"] = cdc.LakehouseMaintenanceCron;
                        AddNode(new IntegrationLineageNodeDto
                        {
                            Id = lhId,
                            Kind = "LakehouseTable",
                            EntityId = cdc.Id,
                            Name = cdc.TargetSinkDestination.Trim(),
                            SubType = $"{cdc.LakehouseTableFormat}{morTag}{branchTag}",
                            Status = cdc.ContractViolated ? "Breached" : "Active",
                            Metadata = lhMeta,
                        });
                        AddEdge(cdcId, lhId, "MaterializesTo", cdc.LakehouseTableFormat.ToString(), ExtractCdcColumnLineage(cdc));
                        if (nodes.ContainsKey(sinkKey))
                        {
                            AddEdge(lhId, sinkKey, "StoredIn", "ObjectStorage");
                        }
                    }
                    else if (nodes.ContainsKey(sinkKey))
                    {
                        AddEdge(cdcId, sinkKey, "WritesTo", cdc.TargetSinkDestination, ExtractCdcColumnLineage(cdc));
                    }
                }
                else if (cdc.TargetMode == CdcTargetMode.RelationalTable && cdc.TargetSinkId.HasValue)
                {
                    var sinkKey = $"source:{cdc.TargetSinkId.Value:D}";
                    if (nodes.ContainsKey(sinkKey))
                    {
                        AddEdge(cdcId, sinkKey, "MaterializesTo", cdc.TargetSinkDestination, ExtractCdcColumnLineage(cdc));
                    }
                }
            }
        }

        if (await HasPermissionAsync(user, SwapMidPermissions.MessageBus.Subscriptions.Default))
        {
            var page = await _messageSubscriptionAppService.GetListAsync(new GetMessageSubscriptionListInput { MaxResultCount = 500 });
            foreach (var sub in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var subId = $"sub:{sub.Id:D}";
                var subMeta = new Dictionary<string, string>(StringComparer.Ordinal);
                var safeHost = TryExtractSafeHost(sub.EndpointUrl);
                if (!string.IsNullOrWhiteSpace(safeHost)) subMeta["endpointHost"] = safeHost;
                if (!string.IsNullOrWhiteSpace(sub.FilterRule)) subMeta["filterRule"] = sub.FilterRule;

                AddNode(new IntegrationLineageNodeDto
                {
                    Id = subId,
                    Kind = "MessageSubscription",
                    EntityId = sub.Id,
                    Name = sub.Name,
                    SubType = sub.DeliveryType.ToString(),
                    Status = sub.IsEnabled ? "Active" : "Disabled",
                    Metadata = subMeta,
                });

                if (!string.IsNullOrWhiteSpace(sub.TopicName))
                {
                    var topicId = $"topic:{sub.TopicName}";
                    if (nodes.ContainsKey(topicId))
                    {
                        AddEdge(topicId, subId, "DeliversTo", sub.FilterRule ?? sub.DeliveryType.ToString());
                    }
                }
            }
        }

        if (await HasPermissionAsync(user, SwapMidPermissions.ApiManagement.ApiGroups.Default))
        {
            var page = await _apiGroupAppService.GetListAsync(new GetApiGroupListInput { MaxResultCount = 500 });
            foreach (var grp in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                AddNode(new IntegrationLineageNodeDto
                {
                    Id = $"apigroup:{grp.Id:D}",
                    Kind = "ApiGroup",
                    EntityId = grp.Id,
                    Name = grp.Name,
                    SubType = "GatewayGroup",
                    Status = grp.IsEnabled ? "Active" : "Disabled",
                });
            }
        }

        if (await HasPermissionAsync(user, SwapMidPermissions.ApiManagement.ApiRoutes.Default))
        {
            var page = await _apiRouteAppService.GetListAsync(new GetApiRouteListInput { MaxResultCount = 500 });
            foreach (var route in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var routeId = $"apiroute:{route.Id:D}";
                var meta = new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = route.Path };
                var upstreamHost = TryExtractSafeHost(route.UpstreamUrl);
                if (!string.IsNullOrWhiteSpace(upstreamHost)) meta["upstreamHost"] = upstreamHost;
                AddNode(new IntegrationLineageNodeDto
                {
                    Id = routeId,
                    Kind = "ApiRoute",
                    EntityId = route.Id,
                    Name = route.Name,
                    SubType = $"{route.HttpMethod} {route.Path}",
                    Status = route.IsEnabled ? "Active" : "Disabled",
                    Metadata = meta,
                });

                if (route.GroupId.HasValue)
                {
                    var groupId = $"apigroup:{route.GroupId.Value:D}";
                    if (nodes.ContainsKey(groupId))
                    {
                        AddEdge(groupId, routeId, "ExposesRoute", route.Path);
                    }
                }
            }
        }

        var nodeList = nodes.Values
            .OrderBy(n => KindSortOrder(n.Kind))
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();
        var counts = nodeList
            .GroupBy(n => n.Kind, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return new IntegrationLineageGraphDto
        {
            GeneratedAtUtc = DateTime.UtcNow,
            NodeCount = nodeList.Count,
            EdgeCount = edges.Count,
            Nodes = nodeList,
            Edges = edges,
            CountsByKind = counts,
            Warnings = warnings,
            Summary = $"跨引擎血缘图谱已构建：{nodeList.Count} 个节点，{edges.Count} 条依赖边。",
        };
    }

    private async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string policy)
        => (await _authorizationService.AuthorizeAsync(user, null, policy)).Succeeded;

    private static (string? Mode, string? TargetTable, List<IntegrationColumnLineageDto> ColumnMappings) ExtractTransformHints(string? transformConfig)
    {
        var cols = new List<IntegrationColumnLineageDto>();
        if (string.IsNullOrWhiteSpace(transformConfig)) return (null, null, cols);
        try
        {
            using var doc = JsonDocument.Parse(transformConfig);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, null, cols);
            var root = doc.RootElement;
            var mode = root.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            var target = root.TryGetProperty("targetTable", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCol(string? s, string? tgt, string type)
            {
                if (string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(tgt)) return;
                var srcTrim = s.Trim();
                var tgtTrim = tgt.Trim();
                if (seen.Add($"{srcTrim}->{tgtTrim}:{type}"))
                {
                    cols.Add(new IntegrationColumnLineageDto { SourceColumn = srcTrim, TargetColumn = tgtTrim, TransformType = type });
                }
            }

            if (root.TryGetProperty("columnMappings", out var cm) && cm.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in cm.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var sc = item.TryGetProperty("sourceColumn", out var scEl) && scEl.ValueKind == JsonValueKind.String ? scEl.GetString() : null;
                    var tc = item.TryGetProperty("targetColumn", out var tcEl) && tcEl.ValueKind == JsonValueKind.String ? tcEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(sc) || string.IsNullOrWhiteSpace(tc)) continue;
                    AddCol(sc, tc, string.Equals(sc.Trim(), tc.Trim(), StringComparison.OrdinalIgnoreCase) ? "identity" : "rename");
                }
            }

            if (root.TryGetProperty("keyColumns", out var kc) && kc.ValueKind == JsonValueKind.Array)
            {
                foreach (var k in kc.EnumerateArray())
                {
                    if (k.ValueKind == JsonValueKind.String) AddCol(k.GetString(), k.GetString(), "business_key");
                }
            }

            if (root.TryGetProperty("watermarkColumn", out var wc) && wc.ValueKind == JsonValueKind.String)
            {
                AddCol(wc.GetString(), wc.GetString(), "watermark");
            }

            return (mode, target, cols);
        }
        catch (JsonException)
        {
            return (null, null, cols);
        }
    }

    private static string? TryExtractSafeHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Authority}"
            : null;
    }

    private static int KindSortOrder(string kind) => kind switch
    {
        "DataSource" => 1,
        "DataPipeline" => 2,
        "CdcStream" => 3,
        "LakehouseTable" => 4,
        "MessageTopic" => 5,
        "MessageSubscription" => 6,
        "ApiGroup" => 7,
        "ApiRoute" => 8,
        _ => 9,
    };
}
