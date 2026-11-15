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
}
