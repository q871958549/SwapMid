using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
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

        // 4. CDC reader user -> diagnose_cdc_streams returns sanitized stream + snapshot state
        var cdcService = host.Services.GetRequiredService<ICdcStreamAppService>();
        cdcService.GetListAsync(Arg.Any<GetCdcStreamListInput>())
            .Returns(new PagedResultDto<CdcStreamDto>(
                1,
                [
                    new CdcStreamDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "orders-pg-cdc",
                        Status = CdcStreamStatus.Active,
                        TargetMode = CdcTargetMode.ExternalSink,
                        TargetSinkName = "ClickHouse Analytics",
                        TargetSinkDestination = "analytics.cdc_orders",
                        TargetSinkKeyColumns = "order_id",
                        CheckpointLsn = "0/1E40000",
                        InitialSnapshotEnabled = true,
                        SnapshotStatus = CdcSnapshotStatus.Completed,
                        SnapshotRowCount = 12,
                        TotalCapturedCount = 15,
                    },
                ]));

        using var cdcReaderClient = host.CreateClient(
            authenticated: true,
            SwapMidPermissions.DataIntegration.CdcStreams.Default);
        var cdcCallRes = await cdcReaderClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 8,
            method = "tools/call",
            @params = new { name = McpToolCatalog.DiagnoseCdcStreams, arguments = new { limit = 5 } },
        });
        cdcCallRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cdcBody = await cdcCallRes.Content.ReadAsStringAsync();
        cdcBody.ShouldContain("orders-pg-cdc");
        cdcBody.ShouldContain("0/1E40000");
        cdcBody.ShouldContain("order_id");
    }

    [Fact]
    public async Task Mcp_copilot_proposes_deterministic_column_mappings_and_evaluates_cdc_data_contracts()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        using var host = new ContractTestHost(
            extraSettings: new Dictionary<string, string?>
            {
                ["Mcp:Enabled"] = "true",
            });

        var preflightService = host.Services.GetRequiredService<IDataPipelineSchemaPreflightAppService>();
        preflightService.PreflightSchemaAsync(Arg.Any<PreflightPipelineSchemaInput>(), Arg.Any<CancellationToken>())
            .Returns(new PipelineSchemaPreflightResultDto
            {
                Compatible = true,
                SourceProvider = "postgresql",
                TargetProvider = "postgresql",
                SourceColumns =
                [
                    new PreflightColumnDto { Name = "id", DataType = "bigint", IsNullable = false },
                    new PreflightColumnDto { Name = "order_no", DataType = "varchar", IsNullable = false },
                    new PreflightColumnDto { Name = "updated_at", DataType = "timestamptz", IsNullable = false },
                ],
                TargetColumns =
                [
                    new PreflightColumnDto { Name = "id", DataType = "bigint", IsNullable = false },
                    new PreflightColumnDto { Name = "orderNo", DataType = "varchar", IsNullable = false },
                    new PreflightColumnDto { Name = "updatedAt", DataType = "timestamptz", IsNullable = false },
                ],
            });

        using var copilotClient = host.CreateClient(
            authenticated: true,
            SwapMidPermissions.DataIntegration.DataPipelines.Default,
            SwapMidPermissions.DataIntegration.CdcStreams.Default);

        var proposeRes = await copilotClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 101,
            method = "tools/call",
            @params = new
            {
                name = McpToolCatalog.ProposePipelineColumnMappings,
                arguments = new
                {
                    sourceId = sourceId.ToString(),
                    targetId = targetId.ToString(),
                    sourceQuery = "SELECT id, order_no, updated_at FROM orders",
                    targetTable = "dw.orders_copy",
                    mode = "upsert",
                },
            },
        });
        proposeRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await proposeRes.Content.ReadAsStringAsync()))
        {
            var innerText = doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
            innerText.ShouldContain("orderNo");
            innerText.ShouldContain("normalized");
            innerText.ShouldContain("updatedAt");
            innerText.ShouldContain("\"readyForDraft\":true");
        }

        var cdcService = host.Services.GetRequiredService<ICdcStreamAppService>();
        cdcService.GetListAsync(Arg.Any<GetCdcStreamListInput>())
            .Returns(new PagedResultDto<CdcStreamDto>(
                2,
                [
                    new CdcStreamDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "orders-sla-breached",
                        Status = CdcStreamStatus.Active,
                        MaxFreshnessLagSeconds = 60,
                        FreshnessLagSeconds = 180,
                        ContractStatus = CdcContractStatus.Breached,
                        ContractViolated = true,
                        ContractViolationSummary = "Freshness exceeded 60s",
                    },
                    new CdcStreamDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "orders-sla-healthy",
                        Status = CdcStreamStatus.Active,
                        MaxFreshnessLagSeconds = 300,
                        FreshnessLagSeconds = 15,
                        ContractStatus = CdcContractStatus.Healthy,
                        ContractViolated = false,
                    },
                ]));

        var contractRes = await copilotClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 102,
            method = "tools/call",
            @params = new
            {
                name = McpToolCatalog.EvaluateCdcDataContracts,
                arguments = new { onlyBreached = true, limit = 10 },
            },
        });
        contractRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await contractRes.Content.ReadAsStringAsync()))
        {
            var innerText = doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
            innerText.ShouldContain("orders-sla-breached");
            innerText.ShouldNotContain("orders-sla-healthy");
            innerText.ShouldContain("\"breachedCount\":1");
            innerText.ShouldContain("\"healthyCount\":1");
        }
    }
}
