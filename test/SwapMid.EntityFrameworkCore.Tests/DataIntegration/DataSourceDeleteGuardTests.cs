using System;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace SwapMid.DataIntegration;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public sealed class DataSourceDeleteGuardTests :
    SwapMidEntityFrameworkCoreTestBase
{
    private readonly IDataSourceAppService _dataSourceAppService;
    private readonly IRepository<DataSource, Guid> _dataSourceRepository;
    private readonly IDataFilter _dataFilter;

    public DataSourceDeleteGuardTests()
    {
        _dataSourceAppService = GetRequiredService<IDataSourceAppService>();
        _dataSourceRepository =
            GetRequiredService<IRepository<DataSource, Guid>>();
        _dataFilter = GetRequiredService<IDataFilter>();
    }

    [Fact]
    public async Task Established_Target_Resource_Group_Should_Block_Delete_Without_Side_Effects()
    {
        // targetResourceGroup 是 receipt-v1 历史目标身份的一部分；即使当前没有管道引用，
        // 删除 DataSource 也会让后续同库别名绕过历史未决执行，因此必须永久失败关闭。
        var target = await CreateRelationalSourceAsync(
            "Protected Receipt Target",
            "physical-db-delete-guard");
        var before = await WithUnitOfWorkAsync(() =>
            _dataSourceRepository.GetAsync(target.Id));
        var beforeConfig = before.Config;
        var beforeConcurrencyStamp = before.ConcurrencyStamp;
        var beforeLastModificationTime = before.LastModificationTime;

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataSourceAppService.DeleteAsync(target.Id));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .DataSourceTargetResourceGroupImmutable);
        await WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<ISoftDelete>())
            {
                var stored = await _dataSourceRepository.GetAsync(target.Id);
                stored.IsDeleted.ShouldBeFalse();
                stored.DeletionTime.ShouldBeNull();
                stored.DeleterId.ShouldBeNull();
                stored.Config.ShouldBe(beforeConfig);
                stored.ConcurrencyStamp.ShouldBe(beforeConcurrencyStamp);
                stored.LastModificationTime.ShouldBe(beforeLastModificationTime);
            }
        });

        var visible = await _dataSourceAppService.GetAsync(target.Id);
        visible.Id.ShouldBe(target.Id);
        visible.Config.ShouldContain("physical-db-delete-guard");
    }

    [Theory]
    [InlineData(DataSourceType.File, "{\"path\":\"safe-delete.csv\"}")]
    [InlineData(
        DataSourceType.RelationalDatabase,
        "{\"provider\":\"sqlserver\",\"host\":\"localhost\",\"port\":1433,\"database\":\"SwapMid\",\"username\":\"reader\"}")]
    public async Task Data_Source_Without_Receipt_Protection_Marker_Should_Remain_Deletable(
        DataSourceType type,
        string config)
    {
        var created = await _dataSourceAppService.CreateAsync(
            new DataSourceCreateUpdateDto
            {
                Name = $"Safe Delete Source {Guid.NewGuid():N}",
                Type = type,
                Config = config,
            });

        await _dataSourceAppService.DeleteAsync(created.Id);

        await WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<ISoftDelete>())
            {
                var stored = await _dataSourceRepository.GetAsync(created.Id);
                stored.IsDeleted.ShouldBeTrue();
                stored.DeletionTime.ShouldNotBeNull();
            }
        });
        (await _dataSourceRepository.FindAsync(created.Id)).ShouldBeNull(
            "正常查询必须继续遵守软删除过滤器");
    }

    private Task<DataSourceDto> CreateRelationalSourceAsync(
        string prefix,
        string targetResourceGroup)
        => _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"{prefix} {Guid.NewGuid():N}",
            Type = DataSourceType.RelationalDatabase,
            Config =
                $"{{\"provider\":\"sqlserver\",\"host\":\"localhost\",\"port\":1433,\"database\":\"SwapMid\",\"username\":\"reader\",\"targetResourceGroup\":\"{targetResourceGroup}\"}}",
        });
}
