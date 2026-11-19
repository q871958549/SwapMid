using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using SwapMid.ApiManagement;
using SwapMid.Controllers.Lineage;
using SwapMid.DataIntegration;
using SwapMid.MessageBus;
using SwapMid.Permissions;
using Volo.Abp.Application.Dtos;

namespace SwapMid.HttpApi.Tests.Lineage;

public class IntegrationLineageTests
{
    [Fact]
    public async Task Lineage_graph_and_impact_endpoints_link_all_engines_and_compute_blast_radius()
    {
        var pgId = Guid.NewGuid();
        var minioId = Guid.NewGuid();
        var pipeId = Guid.NewGuid();
        var cdcId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var routeId = Guid.NewGuid();

        using var host = new ContractTestHost();
        host.Services.GetRequiredService<IDataSourceAppService>()
            .GetListAsync(Arg.Any<GetDataSourceListInput>())
            .Returns(new PagedResultDto<DataSourceDto>(2,
            [
                new DataSourceDto { Id = pgId, Name = "erp-postgres", Type = DataSourceType.RelationalDatabase, IsEnabled = true },
                new DataSourceDto { Id = minioId, Name = "lakehouse-minio", Type = DataSourceType.ObjectStorage, IsEnabled = true },
            ]));
        host.Services.GetRequiredService<IDataPipelineAppService>()
            .GetListAsync(Arg.Any<GetDataPipelineListInput>())
            .Returns(new PagedResultDto<DataPipelineDto>(1,
            [
                new DataPipelineDto
                {
                    Id = pipeId, Name = "daily-orders-etl", SourceId = pgId, TargetId = minioId,
                    TransformConfig = """{"mode":"upsert","targetTable":"dw.orders"}""", Status = PipelineStatus.Active,
                },
            ]));
        host.Services.GetRequiredService<ICdcStreamAppService>()
            .GetListAsync(Arg.Any<GetCdcStreamListInput>())
            .Returns(new PagedResultDto<CdcStreamDto>(1,
            [
                new CdcStreamDto
                {
                    Id = cdcId, Name = "orders-iceberg-wap", SourceId = pgId, SourceType = CdcSourceType.PostgreSqlLogicalReplication,
                    MonitoredTables = "public.orders", TargetMode = CdcTargetMode.ExternalSink,
                    TargetSinkId = minioId, TargetSinkType = DataSourceType.ObjectStorage,
                    TargetSinkDestination = "orders/wap", LakehouseTableFormat = LakehouseTableFormat.ApacheIceberg,
                    LakehouseWriteMode = LakehouseWriteMode.MergeOnReadUpsert, LakehouseWriteBranch = "staging",
                    Status = CdcStreamStatus.Active,
                },
            ]));
        host.Services.GetRequiredService<IMessageTopicAppService>()
            .GetListAsync(Arg.Any<GetMessageTopicListInput>())
            .Returns(new PagedResultDto<MessageTopicDto>(1,
            [
                new MessageTopicDto { Id = topicId, Name = "integration.pipeline.events", Type = TopicType.Broadcast, IsEnabled = true },
            ]));
        host.Services.GetRequiredService<IMessageSubscriptionAppService>()
            .GetListAsync(Arg.Any<GetMessageSubscriptionListInput>())
            .Returns(new PagedResultDto<MessageSubscriptionDto>(1,
            [
                new MessageSubscriptionDto
                {
                    Id = subId, TopicId = topicId, TopicName = "integration.pipeline.events", Name = "ops-alert-webhook",
                    DeliveryType = DeliveryType.Http, EndpointUrl = "https://ops.example.com/hook?token=secret", IsEnabled = true,
                },
            ]));
        host.Services.GetRequiredService<IApiGroupAppService>()
            .GetListAsync(Arg.Any<GetApiGroupListInput>())
            .Returns(new PagedResultDto<ApiGroupDto>(1,
            [
                new ApiGroupDto { Id = groupId, Name = "Orders API", IsEnabled = true },
            ]));
        host.Services.GetRequiredService<IApiRouteAppService>()
            .GetListAsync(Arg.Any<GetApiRouteListInput>())
            .Returns(new PagedResultDto<ApiRouteDto>(1,
            [
                new ApiRouteDto
                {
                    Id = routeId, GroupId = groupId, GroupName = "Orders API", Name = "GetOrders",
                    Path = "/orders", HttpMethod = HttpMethodType.GET, UpstreamUrl = "https://orders.internal/v1", IsEnabled = true,
                },
            ]));

        using var client = host.CreateClient(
            authenticated: true,
            SwapMidPermissions.DataIntegration.DataSources.Default,
            SwapMidPermissions.DataIntegration.DataPipelines.Default,
            SwapMidPermissions.DataIntegration.CdcStreams.Default,
            SwapMidPermissions.MessageBus.Topics.Default,
            SwapMidPermissions.MessageBus.Subscriptions.Default,
            SwapMidPermissions.ApiManagement.ApiGroups.Default,
            SwapMidPermissions.ApiManagement.ApiRoutes.Default);

        var graphRes = await client.GetAsync("/api/v1/integration-lineage");
        graphRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var graphJson = await graphRes.Content.ReadAsStringAsync();
        graphJson.ShouldNotContain("token=secret");
        var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOpts.Converters.Add(new ChinaDateTimeJsonConverter());
        var graph = JsonSerializer.Deserialize<IntegrationLineageGraphDto>(graphJson, jsonOpts)!;
        graph.NodeCount.ShouldBe(9); // 2 sources + 1 pipe + 1 cdc + 1 lakehouse + 1 topic + 1 sub + 1 group + 1 route
        graph.CountsByKind["LakehouseTable"].ShouldBe(1);
        graph.Edges.ShouldContain(e => e.Relation == "MaterializesTo" && e.SourceNodeId == $"cdc:{cdcId:D}");
        graph.Edges.ShouldContain(e => e.Relation == "PublishesTo" && e.SourceNodeId == $"pipeline:{pipeId:D}");

        var impactRes = await client.GetAsync($"/api/v1/integration-lineage/impact?query=erp-postgres&direction=downstream&maxDepth=5");
        impactRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var impact = await impactRes.Content.ReadFromJsonAsync<IntegrationLineageImpactDto>();
        impact.ShouldNotBeNull();
        impact.RootNodeName.ShouldBe("erp-postgres");
        impact.RiskLevel.ShouldBe("high");
        impact.DownstreamCount.ShouldBeGreaterThanOrEqualTo(5); // pipe + cdc + lakehouse + minio + topic + sub
        impact.ImpactedNodes.ShouldContain(i => i.Node.Kind == "LakehouseTable" && i.Node.Name == "orders/wap");
        impact.ImpactedNodes.ShouldContain(i => i.Node.Kind == "MessageSubscription" && i.Node.Name == "ops-alert-webhook");
        impact.Recommendations.ShouldNotBeEmpty();
    }
}
