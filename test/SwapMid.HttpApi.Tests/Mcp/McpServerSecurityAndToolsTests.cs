using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SwapMid.DataIntegration;
using SwapMid.Mcp;
using SwapMid.Permissions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.Application.Dtos;

namespace SwapMid.HttpApi.Tests.Mcp;

public class McpServerSecurityAndToolsTests
{
    [Fact]
    public async Task Mcp_requires_authentication_and_is_fail_closed_by_default()
    {
        using var disabledHost = new ContractTestHost();

        using var anonymous = disabledHost.CreateClient(authenticated: false);
        var unauthRes = await anonymous.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
        });
        unauthRes.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var authed = disabledHost.CreateClient(authenticated: true);
        var disabledRes = await authed.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
        });
        disabledRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Mcp_filters_tools_by_rbac_redacts_datasource_secrets_and_enforces_draft_only_pipelines()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        DataPipelineCreateUpdateDto? capturedDraft = null;

        using var host = new ContractTestHost(
            extraSettings: new Dictionary<string, string?>
            {
                ["Mcp:Enabled"] = "true",
            });

        var dataSourceService = host.Services.GetRequiredService<IDataSourceAppService>();
        dataSourceService.GetListAsync(Arg.Any<GetDataSourceListInput>())
            .Returns(new PagedResultDto<DataSourceDto>(
                1,
                [
                    new DataSourceDto
                    {
                        Id = sourceId,
                        Name = "pg-primary",
                        Type = DataSourceType.RelationalDatabase,
                        Config = "{\"password\":\"TOP-SECRET-DB-PWD\"}",
                        IsEnabled = true,
                        Description = "Primary OLTP",
                    },
                ]));

        var pipelineService = host.Services.GetRequiredService<IDataPipelineAppService>();
        pipelineService.CreateAsync(Arg.Do<DataPipelineCreateUpdateDto>(dto => capturedDraft = dto))
            .Returns(call =>
            {
                var input = call.Arg<DataPipelineCreateUpdateDto>();
                return new DataPipelineDto
                {
                    Id = Guid.NewGuid(),
                    Name = input.Name,
                    Status = PipelineStatus.Draft,
                    ScheduleEnabled = input.ScheduleEnabled,
                };
            });

        // 1. Reader user: only DataSources.Default -> sees ONLY list_datasources, forbidden from draft_data_pipeline
        using var readerClient = host.CreateClient(
            authenticated: true,
            SwapMidPermissions.DataIntegration.DataSources.Default);

        var initRes = await readerClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = "init-1",
            method = "initialize",
        });
        initRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var initJson = await initRes.Content.ReadAsStringAsync();
        initJson.ShouldContain(McpController.ProtocolVersion);

        var notifRes = await readerClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized",
        });
        notifRes.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var listRes = await readerClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list",
        });
        listRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await listRes.Content.ReadAsStringAsync()))
        {
            var toolNames = doc.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(e => e.GetProperty("name").GetString())
                .ToArray();
            toolNames.ShouldBe([McpToolCatalog.ListDatasources]);
        }

        // Calling list_datasources MUST NOT leak Config/password
        var dsCallRes = await readerClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new { name = McpToolCatalog.ListDatasources, arguments = new { limit = 10 } },
        });
        dsCallRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dsBody = await dsCallRes.Content.ReadAsStringAsync();
        dsBody.ShouldContain("pg-primary");
        dsBody.ShouldNotContain("TOP-SECRET-DB-PWD");

        // Calling draft_data_pipeline without DataPipelines.Create -> 403 Forbidden (-32003)
        var forbiddenDraftRes = await readerClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "tools/call",
            @params = new
            {
                name = McpToolCatalog.DraftDataPipeline,
                arguments = new
                {
                    name = "Unauthorized Draft",
                    sourceId = sourceId.ToString(),
                    transformConfig = "{\"sourceQuery\":\"SELECT 1 AS id\",\"targetTable\":\"t\",\"mode\":\"append\"}",
                },
            },
        });
        forbiddenDraftRes.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await forbiddenDraftRes.Content.ReadAsStringAsync()).ShouldContain("-32003");

        // 2. Author user with DataPipelines.Create -> creates Draft pipeline with ScheduleEnabled=false
        using var authorClient = host.CreateClient(
            authenticated: true,
            SwapMidPermissions.DataIntegration.DataPipelines.Create);
        var draftRes = await authorClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 5,
            method = "tools/call",
            @params = new
            {
                name = McpToolCatalog.DraftDataPipeline,
                arguments = new
                {
                    name = "Orders Incremental Upsert Draft",
                    sourceId = sourceId.ToString(),
                    targetId = targetId.ToString(),
                    transformConfig = JsonSerializer.Serialize(new
                    {
                        sourceQuery = "SELECT order_id, amount FROM orders",
                        targetTable = "dw_orders",
                        mode = "upsert",
                        keyColumns = new[] { "order_id" },
                    }),
                },
            },
        });
        draftRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        capturedDraft.ShouldNotBeNull();
        capturedDraft!.ScheduleEnabled.ShouldBeFalse();
        capturedDraft.ScheduleCron.ShouldBeNull();

        // 3. Author user with both DataPipelines.Create & DataSources.Default -> validates ObjectStorage & Kafka destinations
        var objectStorageTargetId = Guid.NewGuid();
        var kafkaTargetId = Guid.NewGuid();
        dataSourceService.GetAsync(objectStorageTargetId)
            .Returns(new DataSourceDto { Id = objectStorageTargetId, Name = "MinIO Lake", Type = DataSourceType.ObjectStorage });
        dataSourceService.GetAsync(kafkaTargetId)
            .Returns(new DataSourceDto { Id = kafkaTargetId, Name = "Kafka Events", Type = DataSourceType.Kafka });

        using var sinkAuthorClient = host.CreateClient(
            authenticated: true,
            SwapMidPermissions.DataIntegration.DataPipelines.Create,
            SwapMidPermissions.DataIntegration.DataSources.Default);

        capturedDraft = null;
        var s3DraftRes = await sinkAuthorClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 6,
            method = "tools/call",
            @params = new
            {
                name = McpToolCatalog.DraftDataPipeline,
                arguments = new
                {
                    name = "Orders Parquet Draft",
                    sourceId = sourceId.ToString(),
                    targetId = objectStorageTargetId.ToString(),
                    transformConfig = JsonSerializer.Serialize(new
                    {
                        sourceQuery = "SELECT order_id, amount FROM orders",
                        targetTable = "erp/daily",
                        mode = "append",
                    }),
                },
            },
        });
        s3DraftRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        capturedDraft.ShouldNotBeNull();
        capturedDraft!.TargetId.ShouldBe(objectStorageTargetId);
        capturedDraft.ScheduleEnabled.ShouldBeFalse();

        capturedDraft = null;
        var kafkaDraftRes = await sinkAuthorClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 7,
            method = "tools/call",
            @params = new
            {
                name = McpToolCatalog.DraftDataPipeline,
                arguments = new
                {
                    name = "Orders Kafka Draft",
                    sourceId = sourceId.ToString(),
                    targetId = kafkaTargetId.ToString(),
                    transformConfig = JsonSerializer.Serialize(new
                    {
                        sourceQuery = "SELECT order_id, amount FROM orders",
                        targetTable = "orders.events",
                        mode = "append",
                    }),
                },
            },
        });
        kafkaDraftRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        capturedDraft.ShouldNotBeNull();
        capturedDraft!.TargetId.ShouldBe(kafkaTargetId);
        capturedDraft.ScheduleEnabled.ShouldBeFalse();
    }
}
