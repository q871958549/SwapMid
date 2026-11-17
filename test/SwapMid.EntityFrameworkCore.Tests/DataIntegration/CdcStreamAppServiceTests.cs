using System;
using System.Threading.Tasks;
using Shouldly;
using SwapMid.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DataIntegration;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class CdcStreamAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly ICdcStreamAppService _cdcAppService;
    private readonly IDataSourceAppService _dataSourceAppService;
    private readonly ICurrentTenant _currentTenant;

    public CdcStreamAppServiceTests()
    {
        _cdcAppService = GetRequiredService<ICdcStreamAppService>();
        _dataSourceAppService = GetRequiredService<IDataSourceAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Should_Create_Update_Filter_ResetCheckpoint_And_Delete_CdcStream()
    {
        var pgSource = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"pg-cdc-src-{Guid.NewGuid():N}"[..32],
            Type = DataSourceType.RelationalDatabase,
            Config = """{"provider":"postgresql","host":"127.0.0.1","port":5432,"database":"swapmid","username":"postgres"}""",
        });

        var created = await _cdcAppService.CreateAsync(new CdcStreamCreateUpdateDto
        {
            Name = $"orders-cdc-{Guid.NewGuid():N}"[..32],
            Description = "Capture order changes",
            SourceId = pgSource.Id,
            PublicationName = "swapmid_orders_pub",
            SlotName = "swapmid_orders_slot",
            MonitoredTables = "etl_demo_source_orders",
            AutoCreatePublicationAndSlot = true,
            PollIntervalSeconds = 10,
            MaxBatchSize = 200,
            TargetMode = CdcTargetMode.MessageBusTopic,
        });

        created.Status.ShouldBe(CdcStreamStatus.Draft);
        created.SourceName.ShouldBe(pgSource.Name);
        created.MonitoredTables.ShouldBe("public.etl_demo_source_orders");
        created.TargetTopicName.ShouldBe("integration.cdc.events");

        var duplicateEx = await Should.ThrowAsync<BusinessException>(() =>
            _cdcAppService.CreateAsync(new CdcStreamCreateUpdateDto
            {
                Name = created.Name,
                SourceId = pgSource.Id,
                PublicationName = "pub2",
                SlotName = "slot2",
                TargetMode = CdcTargetMode.MessageBusTopic,
            }));
        duplicateEx.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.CdcStreamNameAlreadyExists);

        var updated = await _cdcAppService.UpdateAsync(created.Id, new CdcStreamCreateUpdateDto
        {
            Name = created.Name + "-v2",
            Description = "Updated CDC description",
            SourceId = pgSource.Id,
            PublicationName = "swapmid_orders_pub_v2",
            SlotName = "swapmid_orders_slot_v2",
            MonitoredTables = "public.etl_demo_source_orders,public.other_table",
            AutoCreatePublicationAndSlot = false,
            PollIntervalSeconds = 30,
            MaxBatchSize = 500,
            TargetMode = CdcTargetMode.MessageBusTopic,
            TargetTopicName = "integration.cdc.events",
        });

        updated.PollIntervalSeconds.ShouldBe(30);
        updated.AutoCreatePublicationAndSlot.ShouldBeFalse();

        var reset = await _cdcAppService.ResetCheckpointAsync(created.Id, new CdcResetCheckpointInput
        {
            CheckpointLsn = "0/16A2B88",
        });
        reset.CheckpointLsn.ShouldBe("0/16A2B88");

        var list = await _cdcAppService.GetListAsync(new GetCdcStreamListInput
        {
            Filter = "swapmid_orders_pub_v2",
            Sorting = "name asc",
        });
        list.TotalCount.ShouldBe(1);
        list.Items[0].Id.ShouldBe(created.Id);

        await _cdcAppService.DeleteAsync(created.Id);
        var afterDelete = await _cdcAppService.GetListAsync(new GetCdcStreamListInput { Filter = created.Name });
        afterDelete.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Reject_NonPostgreSql_Source_And_Tenant_Context()
    {
        var mysqlSource = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"mysql-src-{Guid.NewGuid():N}"[..32],
            Type = DataSourceType.RelationalDatabase,
            Config = """{"provider":"mysql","host":"127.0.0.1","port":3306,"database":"swapmid","username":"root"}""",
        });

        var providerEx = await Should.ThrowAsync<BusinessException>(() =>
            _cdcAppService.CreateAsync(new CdcStreamCreateUpdateDto
            {
                Name = $"invalid-cdc-{Guid.NewGuid():N}"[..32],
                SourceId = mysqlSource.Id,
                PublicationName = "swapmid_pub",
                SlotName = "swapmid_slot",
                TargetMode = CdcTargetMode.MessageBusTopic,
            }));
        providerEx.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid);

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() =>
                _cdcAppService.GetListAsync(new GetCdcStreamListInput()));
        }
    }

    [Fact]
    public async Task Should_Validate_And_Enrich_ExternalSink_TargetMode()
    {
        var pgSource = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"pg-src-{Guid.NewGuid():N}"[..32],
            Type = DataSourceType.RelationalDatabase,
            Config = """{"provider":"postgresql","host":"127.0.0.1","port":5432,"database":"swapmid","username":"postgres"}""",
        });
        var s3Sink = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"minio-cdc-sink-{Guid.NewGuid():N}"[..32],
            Type = DataSourceType.ObjectStorage,
            Config = """{"endpoint":"http://127.0.0.1:9000","region":"us-east-1","bucket":"datalake","prefix":"cdc","format":"parquet","usePathStyle":true,"accessKey":"minioadmin","secretKey":"minioadmin"}""",
        });

        var invalidSinkEx = await Should.ThrowAsync<BusinessException>(() =>
            _cdcAppService.CreateAsync(new CdcStreamCreateUpdateDto
            {
                Name = $"bad-sink-{Guid.NewGuid():N}"[..32],
                SourceId = pgSource.Id,
                PublicationName = "pub_bad",
                SlotName = "slot_bad",
                TargetMode = CdcTargetMode.ExternalSink,
                TargetSinkId = pgSource.Id,
                TargetSinkDestination = "orders",
            }));
        invalidSinkEx.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid);

        var invalidDestEx = await Should.ThrowAsync<BusinessException>(() =>
            _cdcAppService.CreateAsync(new CdcStreamCreateUpdateDto
            {
                Name = $"bad-dest-{Guid.NewGuid():N}"[..32],
                SourceId = pgSource.Id,
                PublicationName = "pub_bad2",
                SlotName = "slot_bad2",
                TargetMode = CdcTargetMode.ExternalSink,
                TargetSinkId = s3Sink.Id,
                TargetSinkDestination = "../escape",
            }));
        invalidDestEx.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid);

        var created = await _cdcAppService.CreateAsync(new CdcStreamCreateUpdateDto
        {
            Name = $"pg-to-parquet-{Guid.NewGuid():N}"[..32],
            SourceId = pgSource.Id,
            PublicationName = "pub_parquet",
            SlotName = "slot_parquet",
            MonitoredTables = "public.etl_demo_source_orders",
            TargetMode = CdcTargetMode.ExternalSink,
            TargetSinkId = s3Sink.Id,
            TargetSinkDestination = "cdc/orders",
        });

        created.TargetMode.ShouldBe(CdcTargetMode.ExternalSink);
        created.TargetSinkId.ShouldBe(s3Sink.Id);
        created.TargetSinkName.ShouldBe(s3Sink.Name);
        created.TargetSinkType.ShouldBe(DataSourceType.ObjectStorage);
        created.TargetSinkDestination.ShouldBe("cdc/orders");

        var list = await _cdcAppService.GetListAsync(new GetCdcStreamListInput { Filter = "pub_parquet" });
        list.TotalCount.ShouldBe(1);
        list.Items[0].TargetSinkName.ShouldBe(s3Sink.Name);
        list.Items[0].TargetSinkType.ShouldBe(DataSourceType.ObjectStorage);

        var chSink = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"ch-cdc-sink-{Guid.NewGuid():N}"[..32],
            Type = DataSourceType.ClickHouse,
            Config = """{"host":"127.0.0.1","port":8123,"database":"analytics","username":"default"}""",
        });
        var chStream = await _cdcAppService.CreateAsync(new CdcStreamCreateUpdateDto
        {
            Name = $"pg-to-ch-{Guid.NewGuid():N}"[..32],
            SourceId = pgSource.Id,
            PublicationName = "pub_ch",
            SlotName = "slot_ch",
            MonitoredTables = "public.etl_demo_source_orders",
            TargetMode = CdcTargetMode.ExternalSink,
            TargetSinkId = chSink.Id,
            TargetSinkDestination = "cdc_orders_changelog",
            TargetSinkKeyColumns = "order_id, customer_code",
            InitialSnapshotEnabled = true,
        });

        chStream.TargetSinkKeyColumns.ShouldBe("order_id, customer_code");
        chStream.InitialSnapshotEnabled.ShouldBeTrue();
        chStream.SnapshotStatus.ShouldBe(CdcSnapshotStatus.NotStarted);
        chStream.SnapshotRowCount.ShouldBe(0);

        var template = await _cdcAppService.GetSinkTemplateAsync(chStream.Id);
        template.TargetType.ShouldBe(DataSourceType.ClickHouse);
        template.Destination.ShouldBe("cdc_orders_changelog");
        template.Columns.Count.ShouldBe(11);
        template.PartitionKeyExpression.ShouldBe("public.etl_demo_source_orders:order_id=<value>|customer_code=<value>");
        template.ClickHouseDdl.ShouldNotBeNull();
        template.ClickHouseDdl.ShouldContain("CREATE TABLE IF NOT EXISTS `analytics`.`cdc_orders_changelog`");
        template.ClickHouseDdl.ShouldContain("ENGINE = ReplacingMergeTree(`commit_timestamp_utc`)");
        template.ClickHouseDdl.ShouldContain("JSONExtractRaw(coalesce(`after_json`, `before_json`, '{}'), 'order_id')");
        template.SamplePayloadJson.ShouldContain("etl_demo_source_orders");
    }

    [Fact]
    public async Task Should_Validate_And_Persist_RelationalTable_Mirror_And_SchemaDrift_Policies()
    {
        var pgSource = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"pg-mirror-src-{Guid.NewGuid():N}"[..32],
            Type = DataSourceType.RelationalDatabase,
            Config = """{"provider":"postgresql","host":"127.0.0.1","port":5432,"database":"swapmid","username":"postgres"}""",
        });
        var pgTarget = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"pg-mirror-tgt-{Guid.NewGuid():N}"[..32],
            Type = DataSourceType.RelationalDatabase,
            Config = """{"provider":"postgresql","host":"127.0.0.1","port":5432,"database":"swapmid","username":"postgres"}""",
        });

        // Must have single monitored table & non-empty key columns
        var missingKeyEx = await Should.ThrowAsync<BusinessException>(() =>
            _cdcAppService.CreateAsync(new CdcStreamCreateUpdateDto
            {
                Name = $"mirror-no-key-{Guid.NewGuid():N}"[..32],
                SourceId = pgSource.Id,
                PublicationName = "pub_m1",
                SlotName = "slot_m1",
                MonitoredTables = "public.etl_demo_source_orders",
                TargetMode = CdcTargetMode.RelationalTable,
                TargetSinkId = pgTarget.Id,
                TargetSinkDestination = "public.orders_mirror",
                TargetSinkKeyColumns = null,
            }));
        missingKeyEx.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid);

        var multiTableEx = await Should.ThrowAsync<BusinessException>(() =>
            _cdcAppService.CreateAsync(new CdcStreamCreateUpdateDto
            {
                Name = $"mirror-multi-{Guid.NewGuid():N}"[..32],
                SourceId = pgSource.Id,
                PublicationName = "pub_m2",
                SlotName = "slot_m2",
                MonitoredTables = "public.t1,public.t2",
                TargetMode = CdcTargetMode.RelationalTable,
                TargetSinkId = pgTarget.Id,
                TargetSinkDestination = "public.orders_mirror",
                TargetSinkKeyColumns = "id",
            }));
        multiTableEx.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid);

        var created = await _cdcAppService.CreateAsync(new CdcStreamCreateUpdateDto
        {
            Name = $"mirror-ok-{Guid.NewGuid():N}"[..32],
            SourceId = pgSource.Id,
            PublicationName = "pub_m_ok",
            SlotName = "slot_m_ok",
            MonitoredTables = "etl_demo_source_orders",
            TargetMode = CdcTargetMode.RelationalTable,
            TargetSinkId = pgTarget.Id,
            TargetSinkDestination = "public.etl_demo_orders_mirror",
            TargetSinkKeyColumns = "order_id",
            TargetDeletePolicy = CdcDeletePolicy.Ignore,
            SchemaDriftPolicy = CdcSchemaDriftPolicy.PauseAndAlert,
            InitialSnapshotEnabled = true,
        });

        created.TargetMode.ShouldBe(CdcTargetMode.RelationalTable);
        created.TargetSinkId.ShouldBe(pgTarget.Id);
        created.TargetSinkName.ShouldBe(pgTarget.Name);
        created.TargetSinkDestination.ShouldBe("public.etl_demo_orders_mirror");
        created.TargetSinkKeyColumns.ShouldBe("order_id");
        created.TargetDeletePolicy.ShouldBe(CdcDeletePolicy.Ignore);
        created.SchemaDriftPolicy.ShouldBe(CdcSchemaDriftPolicy.PauseAndAlert);
        created.SchemaDriftDetected.ShouldBeFalse();
    }
}
