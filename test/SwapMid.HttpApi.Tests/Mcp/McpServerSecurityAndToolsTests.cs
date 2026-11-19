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
            toolNames.ShouldBe([McpToolCatalog.ListDatasources, McpToolCatalog.AnalyzeLineageImpact]);
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

    [Fact]
    public async Task Mcp_tools_9_and_10_diagnose_webhook_deliveries_and_inspect_lakehouse_snapshots()
    {
        var webhookStreamId = Guid.NewGuid();
        var lakehouseStreamId = Guid.NewGuid();

        using var host = new ContractTestHost(
            extraSettings: new Dictionary<string, string?>
            {
                ["Mcp:Enabled"] = "true",
            });

        var cdcService = host.Services.GetRequiredService<ICdcStreamAppService>();
        cdcService.GetAsync(webhookStreamId).Returns(new CdcStreamDto
        {
            Id = webhookStreamId,
            Name = "orders-webhook-dlq",
            SourceType = CdcSourceType.PushWebhook,
            Status = CdcStreamStatus.Paused,
        });
        cdcService.GetAsync(lakehouseStreamId).Returns(new CdcStreamDto
        {
            Id = lakehouseStreamId,
            Name = "orders-kafka-iceberg",
            SourceType = CdcSourceType.KafkaDebezium,
            TargetMode = CdcTargetMode.ExternalSink,
            TargetSinkType = DataSourceType.ObjectStorage,
            LakehouseTableFormat = LakehouseTableFormat.ApacheIceberg,
            LakehousePartitionStrategy = LakehousePartitionStrategy.DailyCommitUtc,
        });
        cdcService.GetLakehouseHistoryAsync(lakehouseStreamId, Arg.Any<GetCdcLakehouseHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(new CdcLakehouseHistoryDto
            {
                StreamId = lakehouseStreamId,
                Destination = "orders/daily",
                TableFormat = LakehouseTableFormat.ApacheIceberg,
                PartitionStrategy = LakehousePartitionStrategy.DailyCommitUtc,
                DeltaLogDirectory = "warehouse/orders/daily/metadata",
                CurrentVersion = 3,
                SelectedVersion = 3,
                TotalCommitCount = 3,
                SelectedActiveFileCount = 2,
                SelectedTotalBytes = 4096,
                SelectedTotalRecordCount = 10,
                TimeTravelQuerySql = "SELECT * FROM iceberg_scan('s3://lake/warehouse/orders/daily/metadata/v3.metadata.json');",
                Summary = "Apache Iceberg v2 · v3",
            });

        var webhookService = host.Services.GetRequiredService<ICdcWebhookIngressService>();
        webhookService.GetDeliveriesAsync(webhookStreamId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new CdcWebhookDeliveryListDto
            {
                StreamId = webhookStreamId,
                TotalCount = 2,
                ReplayableCount = 1,
                Items =
                [
                    new CdcWebhookDeliveryRecordDto
                    {
                        Id = Guid.NewGuid(),
                        StreamId = webhookStreamId,
                        DeliveryId = "dlq-batch-01",
                        Outcome = CdcWebhookIngestOutcome.Inactive,
                        HttpStatusCode = 409,
                        CanReplay = true,
                        PayloadSizeBytes = 256,
                        ReceivedAtUtc = DateTime.UtcNow,
                    },
                    new CdcWebhookDeliveryRecordDto
                    {
                        Id = Guid.NewGuid(),
                        StreamId = webhookStreamId,
                        DeliveryId = "forged-01",
                        Outcome = CdcWebhookIngestOutcome.Unauthorized,
                        HttpStatusCode = 401,
                        CanReplay = false,
                        PayloadSizeBytes = 128,
                        ReceivedAtUtc = DateTime.UtcNow,
                    },
                ],
            });

        using var client = host.CreateClient(
            authenticated: true,
            SwapMidPermissions.DataIntegration.CdcStreams.Default);

        // Tool 9: diagnose_webhook_deliveries
        var whRes = await client.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 201,
            method = "tools/call",
            @params = new
            {
                name = McpToolCatalog.DiagnoseWebhookDeliveries,
                arguments = new { streamId = webhookStreamId.ToString(), onlyReplayable = true },
            },
        });
        whRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await whRes.Content.ReadAsStringAsync()))
        {
            var text = doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
            text.ShouldContain("\"totalReplayableDeadLetters\":1");
            text.ShouldContain("\"totalUnauthorizedRejections\":1");
            text.ShouldContain("dlq-batch-01");
            text.ShouldNotContain("forged-01");
        }

        // Tool 10: inspect_lakehouse_snapshots
        var lhRes = await client.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 202,
            method = "tools/call",
            @params = new
            {
                name = McpToolCatalog.InspectLakehouseSnapshots,
                arguments = new { streamId = lakehouseStreamId.ToString(), maxCommits = 5 },
            },
        });
        lhRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await lhRes.Content.ReadAsStringAsync()))
        {
            var text = doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
            text.ShouldContain("orders-kafka-iceberg");
            text.ShouldContain("\"partitionStrategy\":1");
            text.ShouldContain("iceberg_scan");
        }

        // Tool 11: export_integration_bundle (requires DataPipelines.Default)
        host.Services.GetRequiredService<IDataSourceAppService>()
            .GetListAsync(Arg.Any<GetDataSourceListInput>())
            .Returns(new PagedResultDto<DataSourceDto>(0, []));
        host.Services.GetRequiredService<IDataPipelineAppService>()
            .GetListAsync(Arg.Any<GetDataPipelineListInput>())
            .Returns(new PagedResultDto<DataPipelineDto>(0, []));
        host.Services.GetRequiredService<ICdcStreamAppService>()
            .GetListAsync(Arg.Any<GetCdcStreamListInput>())
            .Returns(new PagedResultDto<CdcStreamDto>(0, []));

        using var gitOpsClient = host.CreateClient(
            authenticated: true,
            SwapMidPermissions.DataIntegration.DataSources.Default,
            SwapMidPermissions.DataIntegration.DataPipelines.Default,
            SwapMidPermissions.DataIntegration.CdcStreams.Default);

        var bundleRes = await gitOpsClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 203,
            method = "tools/call",
            @params = new
            {
                name = McpToolCatalog.ExportIntegrationBundle,
                arguments = new { format = "yaml" },
            },
        });
        bundleRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await bundleRes.Content.ReadAsStringAsync()))
        {
            var text = doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
            text.ShouldContain("swapmid.io/v1");
            text.ShouldContain("schemaVersion: swapmid.io/v1");
        }

        // Tool 12: plan_lakehouse_maintenance (read-only Dry-Run advisor)
        var maintRes = await client.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 204,
            method = "tools/call",
            @params = new
            {
                name = McpToolCatalog.PlanLakehouseMaintenance,
                arguments = new { streamId = lakehouseStreamId.ToString(), retainLast = 1 },
            },
        });
        maintRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await maintRes.Content.ReadAsStringAsync()))
        {
            var text = doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
            text.ShouldContain("\"dryRun\":true");
            text.ShouldContain("orders-kafka-iceberg");
            text.ShouldContain("recommendations");
        }

        // Tool 13: analyze_lineage_impact (cross-engine topology & blast-radius)
        var lineageRes = await gitOpsClient.PostAsJsonAsync("/api/mcp", new
        {
            jsonrpc = "2.0",
            id = 205,
            method = "tools/call",
            @params = new
            {
                name = McpToolCatalog.AnalyzeLineageImpact,
                arguments = new { direction = "both", maxDepth = 4 },
            },
        });
        lineageRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await lineageRes.Content.ReadAsStringAsync()))
        {
            var text = doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
            text.ShouldContain("\"mode\":\"topology\"");
            text.ShouldContain("nodeCount");
        }
    }
}
