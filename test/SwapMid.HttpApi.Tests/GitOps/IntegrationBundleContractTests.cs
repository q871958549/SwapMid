using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using SwapMid.ApiManagement;
using SwapMid.Controllers.GitOps;
using SwapMid.DataIntegration;
using SwapMid.MessageBus;
using SwapMid.Permissions;
using Volo.Abp.Application.Dtos;
using Xunit;

namespace SwapMid.HttpApi.Tests.GitOps;

public class IntegrationBundleContractTests
{
    [Fact]
    public async Task Export_Diff_And_DryRun_Apply_Should_RoundTrip_SecretFree_Yaml_Bundle()
    {
        using var host = new ContractTestHost();
        var dsService = host.Services.GetRequiredService<IDataSourceAppService>();
        var pipeService = host.Services.GetRequiredService<IDataPipelineAppService>();
        var cdcService = host.Services.GetRequiredService<ICdcStreamAppService>();
        var topicService = host.Services.GetRequiredService<IMessageTopicAppService>();
        var subService = host.Services.GetRequiredService<IMessageSubscriptionAppService>();
        var groupService = host.Services.GetRequiredService<IApiGroupAppService>();
        var routeService = host.Services.GetRequiredService<IApiRouteAppService>();

        var srcId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        dsService.GetListAsync(Arg.Any<GetDataSourceListInput>()).Returns(new PagedResultDto<DataSourceDto>(1,
        [
            new DataSourceDto
            {
                Id = srcId, Name = "orders-pg", Type = DataSourceType.RelationalDatabase,
                Config = """{"provider":"postgresql","host":"db","port":5432,"database":"shop"}""",
                HasSecret = true, IsEnabled = true,
            },
        ]));
        pipeService.GetListAsync(Arg.Any<GetDataPipelineListInput>()).Returns(new PagedResultDto<DataPipelineDto>(0, []));
        cdcService.GetListAsync(Arg.Any<GetCdcStreamListInput>()).Returns(new PagedResultDto<CdcStreamDto>(1,
        [
            new CdcStreamDto
            {
                Id = Guid.NewGuid(), Name = "orders-iceberg-mor", SourceId = srcId, SourceName = "orders-pg",
                SourceType = CdcSourceType.PostgreSqlLogicalReplication, PublicationName = "pub_orders", SlotName = "slot_orders",
                TargetMode = CdcTargetMode.ExternalSink, TargetSinkName = "minio-lake", TargetSinkDestination = "orders/mor",
                TargetSinkKeyColumns = "order_id", LakehouseTableFormat = LakehouseTableFormat.ApacheIceberg,
                LakehousePartitionStrategy = LakehousePartitionStrategy.DailyCommitUtc,
                LakehouseWriteMode = LakehouseWriteMode.MergeOnReadUpsert,
            },
        ]));
        topicService.GetListAsync(Arg.Any<GetMessageTopicListInput>()).Returns(new PagedResultDto<MessageTopicDto>(1,
        [
            new MessageTopicDto { Id = topicId, Name = "shop.orders.events", Type = TopicType.Broadcast, RetryCount = 3, RetryIntervalSeconds = 30, IsEnabled = true },
        ]));
        subService.GetListAsync(Arg.Any<GetMessageSubscriptionListInput>()).Returns(new PagedResultDto<MessageSubscriptionDto>(0, []));
        groupService.GetListAsync(Arg.Any<GetApiGroupListInput>()).Returns(new PagedResultDto<ApiGroupDto>(0, []));
        routeService.GetListAsync(Arg.Any<GetApiRouteListInput>()).Returns(new PagedResultDto<ApiRouteDto>(0, []));

        using var client = host.CreateClient(
            authenticated: true,
            SwapMidPermissions.DataIntegration.DataSources.Default,
            SwapMidPermissions.DataIntegration.DataPipelines.Default,
            SwapMidPermissions.DataIntegration.DataPipelines.Create,
            SwapMidPermissions.DataIntegration.CdcStreams.Default,
            SwapMidPermissions.MessageBus.Topics.Default,
            SwapMidPermissions.MessageBus.Subscriptions.Default,
            SwapMidPermissions.ApiManagement.ApiGroups.Default,
            SwapMidPermissions.ApiManagement.ApiRoutes.Default);

        // 1. Export YAML (zero secrets)
        var exportResp = await client.GetAsync("/api/v1/integration-bundles/export?format=yaml");
        exportResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var exported = await exportResp.Content.ReadFromJsonAsync<IntegrationBundleExportResultDto>(IntegrationBundleYamlCodec.JsonOptions);
        exported.ShouldNotBeNull();
        exported.SchemaVersion.ShouldBe("swapmid.io/v1");
        exported.ResourceCount.ShouldBe(3);
        exported.Content.ShouldContain("schemaVersion: swapmid.io/v1");
        exported.Content.ShouldContain("lakehouseWriteMode: MergeOnReadUpsert");

        // 2. Round-trip Diff on unmodified YAML -> 0 creates, 0 updates, 3 unchanged
        var diffResp = await client.PostAsJsonAsync("/api/v1/integration-bundles/diff", new IntegrationBundleDiffInput
        {
            Content = exported.Content,
        });
        diffResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var diff = await diffResp.Content.ReadFromJsonAsync<IntegrationBundleDiffResultDto>(IntegrationBundleYamlCodec.JsonOptions);
        diff.ShouldNotBeNull();
        diff.CreateCount.ShouldBe(0);
        diff.UpdateCount.ShouldBe(0);
        diff.UnchangedCount.ShouldBe(3);

        // 3. Dry-Run Apply with a brand-new topic -> 1 Create planned, zero writes executed
        var desiredBundle = exported.Bundle;
        desiredBundle.MessageTopics.Add(new BundleMessageTopicItem
        {
            Name = "shop.payments.events",
            Type = TopicType.Broadcast,
            RetryCount = 5,
            RetryIntervalSeconds = 60,
            IsEnabled = true,
        });
        var desiredYaml = IntegrationBundleYamlCodec.SerializeYaml(desiredBundle);

        var applyDryResp = await client.PostAsJsonAsync("/api/v1/integration-bundles/apply", new IntegrationBundleApplyInput
        {
            Content = desiredYaml,
            DryRun = true,
        });
        applyDryResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dryResult = await applyDryResp.Content.ReadFromJsonAsync<IntegrationBundleApplyResultDto>(IntegrationBundleYamlCodec.JsonOptions);
        dryResult.ShouldNotBeNull();
        dryResult.DryRun.ShouldBeTrue();
        dryResult.CreatedCount.ShouldBe(1);
        await topicService.DidNotReceive().CreateAsync(Arg.Any<MessageTopicCreateUpdateDto>());

        // 4. Execute Apply (dryRun: false) -> creates shop.payments.events
        topicService.CreateAsync(Arg.Any<MessageTopicCreateUpdateDto>()).Returns(call =>
        {
            var inp = call.Arg<MessageTopicCreateUpdateDto>();
            return new MessageTopicDto
            {
                Id = Guid.NewGuid(),
                Name = inp.Name,
                Type = inp.Type,
                RetryCount = inp.RetryCount,
                RetryIntervalSeconds = inp.RetryIntervalSeconds,
                IsEnabled = inp.IsEnabled,
            };
        });
        var applyExecResp = await client.PostAsJsonAsync("/api/v1/integration-bundles/apply", new IntegrationBundleApplyInput
        {
            Content = desiredYaml,
            DryRun = false,
            AllowUpdateExisting = false,
        });
        applyExecResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var execResult = await applyExecResp.Content.ReadFromJsonAsync<IntegrationBundleApplyResultDto>(IntegrationBundleYamlCodec.JsonOptions);
        execResult.ShouldNotBeNull();
        execResult.DryRun.ShouldBeFalse();
        execResult.CreatedCount.ShouldBe(1);
        execResult.FailedCount.ShouldBe(0);
        await topicService.Received(1).CreateAsync(Arg.Is<MessageTopicCreateUpdateDto>(x => x.Name == "shop.payments.events"));
    }
}
