using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using SwapMid.ApiManagement;
using SwapMid.DataIntegration;
using SwapMid.MessageBus;
using SwapMid.Permissions;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.Controllers.GitOps;

/// <summary>
/// 声明式 GitOps 集成清单编排器（Export / Diff / Apply）：
/// 基于各业务域应用服务契约跨引擎汇总与按拓扑依赖安全应用（零明文密钥导出、默认 Dry-Run、新建管道/CDC 强制 Draft）。
/// </summary>
public sealed partial class IntegrationBundleOrchestrator : ITransientDependency
{
    private readonly IDataSourceAppService _dataSourceAppService;
    private readonly IDataPipelineAppService _dataPipelineAppService;
    private readonly ICdcStreamAppService _cdcStreamAppService;
    private readonly IMessageTopicAppService _messageTopicAppService;
    private readonly IMessageSubscriptionAppService _messageSubscriptionAppService;
    private readonly IApiGroupAppService _apiGroupAppService;
    private readonly IApiRouteAppService _apiRouteAppService;
    private readonly IAuthorizationService _authorizationService;

    public IntegrationBundleOrchestrator(
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

    public async Task<IntegrationBundleExportResultDto> ExportAsync(ClaimsPrincipal user, string? format)
    {
        var warnings = new List<string>();
        var live = await LoadLiveSnapshotAsync(user, warnings);
        var normalizedFormat = string.Equals(format?.Trim(), "json", StringComparison.OrdinalIgnoreCase) ? "json" : "yaml";
        var content = normalizedFormat == "json"
            ? IntegrationBundleYamlCodec.SerializeJson(live.Bundle)
            : IntegrationBundleYamlCodec.SerializeYaml(live.Bundle);

        var count = live.Bundle.DataSources.Count + live.Bundle.DataPipelines.Count +
                    live.Bundle.CdcStreams.Count + live.Bundle.MessageTopics.Count +
                    live.Bundle.MessageSubscriptions.Count + live.Bundle.ApiGroups.Count +
                    live.Bundle.ApiRoutes.Count;

        return new IntegrationBundleExportResultDto
        {
            SchemaVersion = live.Bundle.SchemaVersion,
            Format = normalizedFormat,
            ResourceCount = count,
            Warnings = warnings,
            Content = content,
            Bundle = live.Bundle,
        };
    }

    public async Task<IntegrationBundleDiffResultDto> DiffAsync(ClaimsPrincipal user, string rawContent)
    {
        var desired = IntegrationBundleYamlCodec.Parse(rawContent);
        var warnings = new List<string>();
        var live = await LoadLiveSnapshotAsync(user, warnings);
        var items = ComputeDiffItems(desired, live.Bundle);

        var creates = items.Count(i => i.Action == "Create");
        var updates = items.Count(i => i.Action == "Update");
        var unchanged = items.Count(i => i.Action == "Unchanged");
        var needsSecret = items.Count(i => i.Action == "NeedsSecret");

        return new IntegrationBundleDiffResultDto
        {
            CreateCount = creates,
            UpdateCount = updates,
            UnchangedCount = unchanged,
            NeedsSecretCount = needsSecret,
            Items = items,
            Warnings = warnings,
            Summary = $"GitOps 对比完成：待新建 {creates} 项，待更新 {updates} 项，无变更 {unchanged} 项，需补密钥 {needsSecret} 项。",
        };
    }

    public async Task<IntegrationBundleApplyResultDto> ApplyAsync(ClaimsPrincipal user, IntegrationBundleApplyInput input)
    {
        var desired = IntegrationBundleYamlCodec.Parse(input.Content);
        var warnings = new List<string>();
        var live = await LoadLiveSnapshotAsync(user, warnings);
        var diffItems = ComputeDiffItems(desired, live.Bundle);

        if (input.DryRun)
        {
            return new IntegrationBundleApplyResultDto
            {
                DryRun = true,
                CreatedCount = diffItems.Count(i => i.Action == "Create"),
                UpdatedCount = input.AllowUpdateExisting ? diffItems.Count(i => i.Action == "Update") : 0,
                SkippedCount = diffItems.Count(i => i.Action is "Unchanged" or "NeedsSecret" || (!input.AllowUpdateExisting && i.Action == "Update")),
                FailedCount = 0,
                Items = diffItems,
                Warnings = warnings,
                Summary = $"Dry-Run 预览完成（零写操作）：将新建 {diffItems.Count(i => i.Action == "Create")} 项，更新 {(input.AllowUpdateExisting ? diffItems.Count(i => i.Action == "Update") : 0)} 项。",
            };
        }

        var results = new List<IntegrationBundleDiffItemDto>();
        await ApplyTopicsAsync(desired, live, input.AllowUpdateExisting, results);
        await ApplyApiGroupsAsync(desired, live, input.AllowUpdateExisting, results);
        await ApplyDataSourcesAsync(desired, live, input.AllowUpdateExisting, results);
        await ApplyPipelinesAsync(desired, live, input.AllowUpdateExisting, results);
        await ApplyCdcStreamsAsync(desired, live, input.AllowUpdateExisting, results);
        await ApplySubscriptionsAsync(desired, live, input.AllowUpdateExisting, results);
        await ApplyApiRoutesAsync(desired, live, input.AllowUpdateExisting, results);

        var created = results.Count(r => r.Action == "Created");
        var updated = results.Count(r => r.Action == "Updated");
        var skipped = results.Count(r => r.Action is "Unchanged" or "Skipped" or "NeedsSecret");
        var failed = results.Count(r => r.Action == "Failed");

        return new IntegrationBundleApplyResultDto
        {
            DryRun = false,
            CreatedCount = created,
            UpdatedCount = updated,
            SkippedCount = skipped,
            FailedCount = failed,
            Items = results,
            Warnings = warnings,
            Summary = $"GitOps 应用完成：新建 {created} 项，更新 {updated} 项，跳过 {skipped} 项，失败 {failed} 项（新建管道与 CDC 流默认保持 Draft 状态）。",
        };
    }

    private sealed class LiveSnapshot
    {
        public IntegrationBundleDocument Bundle { get; } = new();
        public Dictionary<string, DataSourceDto> SourcesByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DataPipelineDto> PipelinesByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, CdcStreamDto> CdcStreamsByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, MessageTopicDto> TopicsByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, MessageSubscriptionDto> SubscriptionsByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ApiGroupDto> ApiGroupsByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ApiRouteDto> ApiRoutesByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<LiveSnapshot> LoadLiveSnapshotAsync(ClaimsPrincipal user, List<string> warnings)
    {
        var snap = new LiveSnapshot();
        var version = typeof(IntegrationBundleOrchestrator).Assembly.GetName().Version?.ToString(3) ?? "2.6.0";
        snap.Bundle.PlatformVersion = version;
        snap.Bundle.ExportedAtUtc = DateTime.UtcNow.ToString("O");

        if (await HasPermissionAsync(user, SwapMidPermissions.DataIntegration.DataSources.Default))
        {
            var page = await _dataSourceAppService.GetListAsync(new GetDataSourceListInput { MaxResultCount = 500 });
            foreach (var ds in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                snap.SourcesByName[ds.Name] = ds;
                snap.Bundle.DataSources.Add(new BundleDataSourceItem
                {
                    Name = ds.Name, Description = ds.Description, Type = ds.Type,
                    Config = ds.Config, HasSecret = ds.HasSecret, IsEnabled = ds.IsEnabled,
                });
            }
        }
        else warnings.Add("缺少 DataSources 读取权限，已跳过数据源导出。");

        if (await HasPermissionAsync(user, SwapMidPermissions.DataIntegration.DataPipelines.Default))
        {
            var page = await _dataPipelineAppService.GetListAsync(new GetDataPipelineListInput { MaxResultCount = 500 });
            foreach (var p in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                snap.PipelinesByName[p.Name] = p;
                snap.Bundle.DataPipelines.Add(new BundleDataPipelineItem
                {
                    Name = p.Name, Description = p.Description,
                    SourceName = p.SourceName ?? string.Empty, TargetName = p.TargetName,
                    TransformConfig = p.TransformConfig, ScheduleCron = p.ScheduleCron, ScheduleEnabled = p.ScheduleEnabled,
                });
            }
        }
        else warnings.Add("缺少 DataPipelines 读取权限，已跳过数据管道导出。");

        if (await HasPermissionAsync(user, SwapMidPermissions.DataIntegration.CdcStreams.Default))
        {
            var page = await _cdcStreamAppService.GetListAsync(new GetCdcStreamListInput { MaxResultCount = 500 });
            foreach (var c in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                snap.CdcStreamsByName[c.Name] = c;
                snap.Bundle.CdcStreams.Add(new BundleCdcStreamItem
                {
                    Name = c.Name, Description = c.Description, SourceName = c.SourceName ?? string.Empty,
                    SourceType = c.SourceType, PublicationName = c.PublicationName, SlotName = c.SlotName,
                    MonitoredTables = c.MonitoredTables, AutoCreatePublicationAndSlot = c.AutoCreatePublicationAndSlot,
                    PollIntervalSeconds = c.PollIntervalSeconds, MaxBatchSize = c.MaxBatchSize,
                    TargetMode = c.TargetMode, TargetTopicName = c.TargetTopicName,
                    TargetPipelineName = c.TargetPipelineName, TargetSinkName = c.TargetSinkName,
                    TargetSinkDestination = c.TargetSinkDestination, TargetSinkKeyColumns = c.TargetSinkKeyColumns,
                    TargetDeletePolicy = c.TargetDeletePolicy, LakehouseTableFormat = c.LakehouseTableFormat,
                    LakehousePartitionStrategy = c.LakehousePartitionStrategy, LakehouseWriteMode = c.LakehouseWriteMode,
                    LakehouseCompactMinFiles = c.LakehouseCompactMinFiles, CatalogType = c.CatalogType,
                    CatalogSinkName = c.CatalogSinkName, CatalogNamespace = c.CatalogNamespace,
                    CatalogTableName = c.CatalogTableName, SchemaDriftPolicy = c.SchemaDriftPolicy,
                    InitialSnapshotEnabled = c.InitialSnapshotEnabled, MaxFreshnessLagSeconds = c.MaxFreshnessLagSeconds,
                    MaxAllowedWalLagBytes = c.MaxAllowedWalLagBytes, RequiredColumns = c.RequiredColumns,
                });
            }
        }

        if (await HasPermissionAsync(user, SwapMidPermissions.MessageBus.Topics.Default))
        {
            var page = await _messageTopicAppService.GetListAsync(new GetMessageTopicListInput { MaxResultCount = 500 });
            foreach (var t in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                snap.TopicsByName[t.Name] = t;
                snap.Bundle.MessageTopics.Add(new BundleMessageTopicItem
                {
                    Name = t.Name, Description = t.Description, Type = t.Type,
                    RetryCount = t.RetryCount, RetryIntervalSeconds = t.RetryIntervalSeconds, IsEnabled = t.IsEnabled,
                });
            }
        }

        if (await HasPermissionAsync(user, SwapMidPermissions.MessageBus.Subscriptions.Default))
        {
            var page = await _messageSubscriptionAppService.GetListAsync(new GetMessageSubscriptionListInput { MaxResultCount = 500 });
            foreach (var s in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                snap.SubscriptionsByName[s.Name] = s;
                snap.Bundle.MessageSubscriptions.Add(new BundleMessageSubscriptionItem
                {
                    Name = s.Name, TopicName = s.TopicName ?? string.Empty, DeliveryType = s.DeliveryType,
                    EndpointUrl = s.EndpointUrl, ExchangeName = s.ExchangeName, RoutingKey = s.RoutingKey,
                    FilterRule = s.FilterRule, HasSecret = s.HasSecret, IsEnabled = s.IsEnabled,
                });
            }
        }

        if (await HasPermissionAsync(user, SwapMidPermissions.ApiManagement.ApiGroups.Default))
        {
            var page = await _apiGroupAppService.GetListAsync(new GetApiGroupListInput { MaxResultCount = 500 });
            foreach (var g in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                snap.ApiGroupsByName[g.Name] = g;
                snap.Bundle.ApiGroups.Add(new BundleApiGroupItem
                {
                    Name = g.Name, Description = g.Description, Order = g.Order, IsEnabled = g.IsEnabled,
                });
            }
        }

        if (await HasPermissionAsync(user, SwapMidPermissions.ApiManagement.ApiRoutes.Default))
        {
            var page = await _apiRouteAppService.GetListAsync(new GetApiRouteListInput { MaxResultCount = 500 });
            foreach (var r in page.Items.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                snap.ApiRoutesByName[r.Name] = r;
                snap.Bundle.ApiRoutes.Add(new BundleApiRouteItem
                {
                    Name = r.Name, Description = r.Description, GroupName = r.GroupName,
                    Path = r.Path, HttpMethod = r.HttpMethod, UpstreamUrl = r.UpstreamUrl,
                    Order = r.Order, IsEnabled = r.IsEnabled,
                });
            }
        }

        return snap;
    }

    private async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string policy)
        => (await _authorizationService.AuthorizeAsync(user, null, policy)).Succeeded;

    private static List<IntegrationBundleDiffItemDto> ComputeDiffItems(
        IntegrationBundleDocument desired,
        IntegrationBundleDocument current)
    {
        var items = new List<IntegrationBundleDiffItemDto>();
        DiffMap("DataSource", desired.DataSources, current.DataSources, x => x.Name, items,
            (d, c) =>
            {
                var ch = new List<string>();
                if (d.Type != c.Type) ch.Add("type");
                if (!string.Equals(d.Config, c.Config, StringComparison.Ordinal)) ch.Add("config");
                if (d.IsEnabled != c.IsEnabled) ch.Add("isEnabled");
                return ch;
            },
            needsSecretPredicate: d => d.HasSecret && !current.DataSources.Any(c => string.Equals(c.Name, d.Name, StringComparison.OrdinalIgnoreCase)));

        DiffMap("MessageTopic", desired.MessageTopics, current.MessageTopics, x => x.Name, items,
            (d, c) =>
            {
                var ch = new List<string>();
                if (d.Type != c.Type) ch.Add("type");
                if (d.RetryCount != c.RetryCount) ch.Add("retryCount");
                if (d.RetryIntervalSeconds != c.RetryIntervalSeconds) ch.Add("retryIntervalSeconds");
                if (d.IsEnabled != c.IsEnabled) ch.Add("isEnabled");
                return ch;
            });

        DiffMap("ApiGroup", desired.ApiGroups, current.ApiGroups, x => x.Name, items,
            (d, c) =>
            {
                var ch = new List<string>();
                if (d.Order != c.Order) ch.Add("order");
                if (d.IsEnabled != c.IsEnabled) ch.Add("isEnabled");
                return ch;
            });

        DiffMap("DataPipeline", desired.DataPipelines, current.DataPipelines, x => x.Name, items,
            (d, c) =>
            {
                var ch = new List<string>();
                if (!string.Equals(d.SourceName, c.SourceName, StringComparison.OrdinalIgnoreCase)) ch.Add("sourceName");
                if (!string.Equals(d.TargetName, c.TargetName, StringComparison.OrdinalIgnoreCase)) ch.Add("targetName");
                if (!string.Equals(d.TransformConfig, c.TransformConfig, StringComparison.Ordinal)) ch.Add("transformConfig");
                if (!string.Equals(d.ScheduleCron, c.ScheduleCron, StringComparison.Ordinal)) ch.Add("scheduleCron");
                return ch;
            });

        DiffMap("CdcStream", desired.CdcStreams, current.CdcStreams, x => x.Name, items,
            (d, c) =>
            {
                var ch = new List<string>();
                if (d.SourceType != c.SourceType) ch.Add("sourceType");
                if (d.TargetMode != c.TargetMode) ch.Add("targetMode");
                if (d.LakehouseTableFormat != c.LakehouseTableFormat) ch.Add("lakehouseTableFormat");
                if (d.LakehousePartitionStrategy != c.LakehousePartitionStrategy) ch.Add("lakehousePartitionStrategy");
                if (d.LakehouseWriteMode != c.LakehouseWriteMode) ch.Add("lakehouseWriteMode");
                if (!string.Equals(d.MonitoredTables, c.MonitoredTables, StringComparison.Ordinal)) ch.Add("monitoredTables");
                if (!string.Equals(d.TargetSinkDestination, c.TargetSinkDestination, StringComparison.Ordinal)) ch.Add("targetSinkDestination");
                return ch;
            });

        DiffMap("MessageSubscription", desired.MessageSubscriptions, current.MessageSubscriptions, x => x.Name, items,
            (d, c) =>
            {
                var ch = new List<string>();
                if (!string.Equals(d.TopicName, c.TopicName, StringComparison.OrdinalIgnoreCase)) ch.Add("topicName");
                if (d.DeliveryType != c.DeliveryType) ch.Add("deliveryType");
                if (!string.Equals(d.EndpointUrl, c.EndpointUrl, StringComparison.Ordinal)) ch.Add("endpointUrl");
                if (!string.Equals(d.FilterRule, c.FilterRule, StringComparison.Ordinal)) ch.Add("filterRule");
                return ch;
            });

        DiffMap("ApiRoute", desired.ApiRoutes, current.ApiRoutes, x => x.Name, items,
            (d, c) =>
            {
                var ch = new List<string>();
                if (!string.Equals(d.Path, c.Path, StringComparison.Ordinal)) ch.Add("path");
                if (d.HttpMethod != c.HttpMethod) ch.Add("httpMethod");
                if (!string.Equals(d.UpstreamUrl, c.UpstreamUrl, StringComparison.Ordinal)) ch.Add("upstreamUrl");
                if (d.IsEnabled != c.IsEnabled) ch.Add("isEnabled");
                return ch;
            });

        return items;
    }

    private static void DiffMap<T>(
        string kind,
        IReadOnlyList<T> desired,
        IReadOnlyList<T> current,
        Func<T, string> nameSelector,
        List<IntegrationBundleDiffItemDto> output,
        Func<T, T, List<string>> compareFields,
        Func<T, bool>? needsSecretPredicate = null)
    {
        var currentByName = current.ToDictionary(nameSelector, StringComparer.OrdinalIgnoreCase);
        foreach (var item in desired)
        {
            var name = nameSelector(item);
            if (needsSecretPredicate?.Invoke(item) == true)
            {
                output.Add(new IntegrationBundleDiffItemDto
                {
                    Kind = kind, Name = name, Action = "NeedsSecret",
                    Note = "该数据源包含脱敏密钥，在目标环境新建前需通过控制台或环境变量注入实际凭据。",
                });
                continue;
            }

            if (!currentByName.TryGetValue(name, out var existing))
            {
                output.Add(new IntegrationBundleDiffItemDto { Kind = kind, Name = name, Action = "Create" });
                continue;
            }

            var changed = compareFields(item, existing);
            output.Add(new IntegrationBundleDiffItemDto
            {
                Kind = kind, Name = name,
                Action = changed.Count > 0 ? "Update" : "Unchanged",
                ChangedFields = changed,
            });
        }
    }
}
