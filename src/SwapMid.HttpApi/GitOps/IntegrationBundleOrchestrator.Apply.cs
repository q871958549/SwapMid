using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwapMid.ApiManagement;
using SwapMid.DataIntegration;
using SwapMid.MessageBus;
using Volo.Abp;

namespace SwapMid.Controllers.GitOps;

public sealed partial class IntegrationBundleOrchestrator
{
    private async Task ApplyTopicsAsync(IntegrationBundleDocument d, LiveSnapshot live, bool allowUpdate, List<IntegrationBundleDiffItemDto> results)
    {
        foreach (var item in d.MessageTopics)
        {
            await TryApplyResourceAsync("MessageTopic", item.Name, live.TopicsByName.ContainsKey(item.Name), allowUpdate, results,
                async () => live.TopicsByName[item.Name] = await _messageTopicAppService.CreateAsync(new MessageTopicCreateUpdateDto
                {
                    Name = item.Name, Description = item.Description, Type = item.Type,
                    RetryCount = item.RetryCount, RetryIntervalSeconds = item.RetryIntervalSeconds, IsEnabled = item.IsEnabled,
                }),
                async () => live.TopicsByName[item.Name] = await _messageTopicAppService.UpdateAsync(live.TopicsByName[item.Name].Id, new MessageTopicCreateUpdateDto
                {
                    Name = item.Name, Description = item.Description, Type = item.Type,
                    RetryCount = item.RetryCount, RetryIntervalSeconds = item.RetryIntervalSeconds, IsEnabled = item.IsEnabled,
                }));
        }
    }

    private async Task ApplyApiGroupsAsync(IntegrationBundleDocument d, LiveSnapshot live, bool allowUpdate, List<IntegrationBundleDiffItemDto> results)
    {
        foreach (var item in d.ApiGroups)
        {
            await TryApplyResourceAsync("ApiGroup", item.Name, live.ApiGroupsByName.ContainsKey(item.Name), allowUpdate, results,
                async () => live.ApiGroupsByName[item.Name] = await _apiGroupAppService.CreateAsync(new ApiGroupCreateUpdateDto
                {
                    Name = item.Name, Description = item.Description, Order = item.Order, IsEnabled = item.IsEnabled,
                }),
                async () => live.ApiGroupsByName[item.Name] = await _apiGroupAppService.UpdateAsync(live.ApiGroupsByName[item.Name].Id, new ApiGroupCreateUpdateDto
                {
                    Name = item.Name, Description = item.Description, Order = item.Order, IsEnabled = item.IsEnabled,
                }));
        }
    }

    private async Task ApplyDataSourcesAsync(IntegrationBundleDocument d, LiveSnapshot live, bool allowUpdate, List<IntegrationBundleDiffItemDto> results)
    {
        foreach (var item in d.DataSources)
        {
            var exists = live.SourcesByName.ContainsKey(item.Name);
            if (!exists && item.HasSecret)
            {
                results.Add(new IntegrationBundleDiffItemDto
                {
                    Kind = "DataSource", Name = item.Name, Action = "NeedsSecret",
                    Note = "跳过新建：请先在目标环境安全配置含密钥的数据源凭据。",
                });
                continue;
            }

            await TryApplyResourceAsync("DataSource", item.Name, exists, allowUpdate, results,
                async () => live.SourcesByName[item.Name] = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
                {
                    Name = item.Name, Description = item.Description, Type = item.Type, Config = item.Config, IsEnabled = item.IsEnabled,
                }),
                async () => live.SourcesByName[item.Name] = await _dataSourceAppService.UpdateAsync(live.SourcesByName[item.Name].Id, new DataSourceCreateUpdateDto
                {
                    Name = item.Name, Description = item.Description, Type = item.Type, Config = item.Config, IsEnabled = item.IsEnabled,
                }));
        }
    }

    private async Task ApplyPipelinesAsync(IntegrationBundleDocument d, LiveSnapshot live, bool allowUpdate, List<IntegrationBundleDiffItemDto> results)
    {
        foreach (var item in d.DataPipelines)
        {
            if (!live.SourcesByName.TryGetValue(item.SourceName, out var src))
            {
                results.Add(new IntegrationBundleDiffItemDto { Kind = "DataPipeline", Name = item.Name, Action = "Failed", Note = $"未找到源数据源「{item.SourceName}」。" });
                continue;
            }

            Guid? targetId = null;
            if (!string.IsNullOrWhiteSpace(item.TargetName))
            {
                if (!live.SourcesByName.TryGetValue(item.TargetName, out var tgt))
                {
                    results.Add(new IntegrationBundleDiffItemDto { Kind = "DataPipeline", Name = item.Name, Action = "Failed", Note = $"未找到目标数据源「{item.TargetName}」。" });
                    continue;
                }

                targetId = tgt.Id;
            }

            var dto = new DataPipelineCreateUpdateDto
            {
                Name = item.Name, Description = item.Description, SourceId = src.Id, TargetId = targetId,
                TransformConfig = item.TransformConfig, ScheduleCron = item.ScheduleCron, ScheduleEnabled = false,
            };
            await TryApplyResourceAsync("DataPipeline", item.Name, live.PipelinesByName.ContainsKey(item.Name), allowUpdate, results,
                async () => live.PipelinesByName[item.Name] = await _dataPipelineAppService.CreateAsync(dto),
                async () => live.PipelinesByName[item.Name] = await _dataPipelineAppService.UpdateAsync(live.PipelinesByName[item.Name].Id, dto));
        }
    }

    private async Task ApplyCdcStreamsAsync(IntegrationBundleDocument d, LiveSnapshot live, bool allowUpdate, List<IntegrationBundleDiffItemDto> results)
    {
        foreach (var item in d.CdcStreams)
        {
            if (!live.SourcesByName.TryGetValue(item.SourceName, out var src))
            {
                results.Add(new IntegrationBundleDiffItemDto { Kind = "CdcStream", Name = item.Name, Action = "Failed", Note = $"未找到 CDC 源数据源「{item.SourceName}」。" });
                continue;
            }

            var dto = new CdcStreamCreateUpdateDto
            {
                Name = item.Name, Description = item.Description, SourceId = src.Id, SourceType = item.SourceType,
                PublicationName = item.PublicationName, SlotName = item.SlotName, MonitoredTables = item.MonitoredTables,
                AutoCreatePublicationAndSlot = item.AutoCreatePublicationAndSlot, PollIntervalSeconds = item.PollIntervalSeconds,
                MaxBatchSize = item.MaxBatchSize, TargetMode = item.TargetMode, TargetTopicName = item.TargetTopicName,
                TargetPipelineId = !string.IsNullOrWhiteSpace(item.TargetPipelineName) && live.PipelinesByName.TryGetValue(item.TargetPipelineName, out var tp) ? tp.Id : null,
                TargetSinkId = !string.IsNullOrWhiteSpace(item.TargetSinkName) && live.SourcesByName.TryGetValue(item.TargetSinkName, out var ts) ? ts.Id : null,
                TargetSinkDestination = item.TargetSinkDestination, TargetSinkKeyColumns = item.TargetSinkKeyColumns,
                TargetDeletePolicy = item.TargetDeletePolicy, LakehouseTableFormat = item.LakehouseTableFormat,
                LakehousePartitionStrategy = item.LakehousePartitionStrategy, LakehouseWriteMode = item.LakehouseWriteMode,
                LakehouseWriteBranch = item.LakehouseWriteBranch, LakehouseMaintenanceCron = item.LakehouseMaintenanceCron,
                LakehouseExpireRetainLast = item.LakehouseExpireRetainLast, LakehouseExpireOlderThanMinutes = item.LakehouseExpireOlderThanMinutes,
                LakehouseDeleteOrphanedFiles = item.LakehouseDeleteOrphanedFiles,
                LakehouseCompactMinFiles = item.LakehouseCompactMinFiles, CatalogType = item.CatalogType,
                CatalogSinkId = !string.IsNullOrWhiteSpace(item.CatalogSinkName) && live.SourcesByName.TryGetValue(item.CatalogSinkName, out var cs) ? cs.Id : null,
                CatalogNamespace = item.CatalogNamespace, CatalogTableName = item.CatalogTableName,
                SchemaDriftPolicy = item.SchemaDriftPolicy, InitialSnapshotEnabled = item.InitialSnapshotEnabled,
                MaxFreshnessLagSeconds = item.MaxFreshnessLagSeconds, MaxAllowedWalLagBytes = item.MaxAllowedWalLagBytes,
                RequiredColumns = item.RequiredColumns,
            };
            await TryApplyResourceAsync("CdcStream", item.Name, live.CdcStreamsByName.ContainsKey(item.Name), allowUpdate, results,
                async () => live.CdcStreamsByName[item.Name] = await _cdcStreamAppService.CreateAsync(dto),
                async () => live.CdcStreamsByName[item.Name] = await _cdcStreamAppService.UpdateAsync(live.CdcStreamsByName[item.Name].Id, dto));
        }
    }

    private async Task ApplySubscriptionsAsync(IntegrationBundleDocument d, LiveSnapshot live, bool allowUpdate, List<IntegrationBundleDiffItemDto> results)
    {
        foreach (var item in d.MessageSubscriptions)
        {
            if (!live.TopicsByName.TryGetValue(item.TopicName, out var topic))
            {
                results.Add(new IntegrationBundleDiffItemDto { Kind = "MessageSubscription", Name = item.Name, Action = "Failed", Note = $"未找到消息主题「{item.TopicName}」。" });
                continue;
            }

            var dto = new MessageSubscriptionCreateUpdateDto
            {
                TopicId = topic.Id, Name = item.Name, DeliveryType = item.DeliveryType,
                EndpointUrl = item.EndpointUrl, ExchangeName = item.ExchangeName, RoutingKey = item.RoutingKey,
                FilterRule = item.FilterRule, IsEnabled = item.IsEnabled,
            };
            await TryApplyResourceAsync("MessageSubscription", item.Name, live.SubscriptionsByName.ContainsKey(item.Name), allowUpdate, results,
                async () => live.SubscriptionsByName[item.Name] = await _messageSubscriptionAppService.CreateAsync(dto),
                async () => live.SubscriptionsByName[item.Name] = await _messageSubscriptionAppService.UpdateAsync(live.SubscriptionsByName[item.Name].Id, dto));
        }
    }

    private async Task ApplyApiRoutesAsync(IntegrationBundleDocument d, LiveSnapshot live, bool allowUpdate, List<IntegrationBundleDiffItemDto> results)
    {
        foreach (var item in d.ApiRoutes)
        {
            Guid? groupId = !string.IsNullOrWhiteSpace(item.GroupName) && live.ApiGroupsByName.TryGetValue(item.GroupName, out var grp)
                ? grp.Id
                : null;
            var dto = new ApiRouteCreateUpdateDto
            {
                GroupId = groupId, Name = item.Name, Description = item.Description,
                Path = item.Path, HttpMethod = item.HttpMethod, UpstreamUrl = item.UpstreamUrl,
                Order = item.Order, IsEnabled = item.IsEnabled,
            };
            await TryApplyResourceAsync("ApiRoute", item.Name, live.ApiRoutesByName.ContainsKey(item.Name), allowUpdate, results,
                async () => live.ApiRoutesByName[item.Name] = await _apiRouteAppService.CreateAsync(dto),
                async () => live.ApiRoutesByName[item.Name] = await _apiRouteAppService.UpdateAsync(live.ApiRoutesByName[item.Name].Id, dto));
        }
    }

    private static async Task TryApplyResourceAsync(
        string kind,
        string name,
        bool exists,
        bool allowUpdate,
        List<IntegrationBundleDiffItemDto> results,
        Func<Task> createAsync,
        Func<Task> updateAsync)
    {
        if (exists && !allowUpdate)
        {
            results.Add(new IntegrationBundleDiffItemDto { Kind = kind, Name = name, Action = "Skipped", Note = "已存在同名资源（未勾选允许覆盖更新）。" });
            return;
        }

        try
        {
            if (exists)
            {
                await updateAsync();
                results.Add(new IntegrationBundleDiffItemDto { Kind = kind, Name = name, Action = "Updated" });
            }
            else
            {
                await createAsync();
                results.Add(new IntegrationBundleDiffItemDto { Kind = kind, Name = name, Action = "Created" });
            }
        }
        catch (BusinessException bex)
        {
            results.Add(new IntegrationBundleDiffItemDto { Kind = kind, Name = name, Action = "Failed", Note = bex.Code ?? "BusinessValidationFailed" });
        }
        catch (Exception ex)
        {
            results.Add(new IntegrationBundleDiffItemDto { Kind = kind, Name = name, Action = "Failed", Note = ex.GetType().Name });
        }
    }
}
