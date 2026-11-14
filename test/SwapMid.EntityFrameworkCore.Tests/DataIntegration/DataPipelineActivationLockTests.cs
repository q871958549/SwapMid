using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using SwapMid.EntityFrameworkCore.DataIntegration;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DataIntegration;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public sealed class DataPipelineActivationLockTests
    : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IDataSourceAppService _dataSourceAppService;
    private readonly IDataPipelineAppService _dataPipelineAppService;
    private readonly IDataPipelineRepository _pipelineRepository;
    private readonly IRepository<PipelineExecution, Guid> _executionRepository;
    private readonly FakeEtlPipelineRunner _etlRunner;
    private readonly TestDataPipelineExecutionFence _executionFence;
    private readonly FakeEtlTargetReceiptStore _receiptStore;
    private readonly TestPipelineTerminalStoreTopologyGuard _topologyGuard;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public DataPipelineActivationLockTests()
    {
        _dataSourceAppService = GetRequiredService<IDataSourceAppService>();
        _dataPipelineAppService = GetRequiredService<IDataPipelineAppService>();
        _pipelineRepository = GetRequiredService<IDataPipelineRepository>();
        _executionRepository = GetRequiredService<
            IRepository<PipelineExecution, Guid>>();
        _etlRunner = (FakeEtlPipelineRunner)GetRequiredService<
            IEtlPipelineRunner>();
        _executionFence = GetRequiredService<
            TestDataPipelineExecutionFence>();
        _receiptStore = GetRequiredService<FakeEtlTargetReceiptStore>();
        _topologyGuard = GetRequiredService<
            TestPipelineTerminalStoreTopologyGuard>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        _etlRunner.Reset();
        _executionFence.Reset();
        _receiptStore.Reset();
        _topologyGuard.Reset();
    }

    [Fact]
    public void 数据源行锁必须按稳定Guid顺序且同源目标只锁一次()
    {
        var first = Guid.Parse("10000000-0000-0000-0000-000000000000");
        var second = Guid.Parse("20000000-0000-0000-0000-000000000000");

        var forward = EfCoreDataPipelineRepository
            .BuildActivationDataSourceLockOrder(first, second);
        var reversed = EfCoreDataPipelineRepository
            .BuildActivationDataSourceLockOrder(second, first);

        forward.ShouldBe(new[] { first, second }.OrderBy(id => id));
        reversed.ShouldBe(forward,
            "源/目标角色互换后仍须按同一全局顺序争锁，避免交叉激活死锁");
        EfCoreDataPipelineRepository
            .BuildActivationDataSourceLockOrder(first, first)
            .ShouldBe(new[] { first });
        EfCoreDataPipelineRepository
            .BuildActivationDataSourceLockOrder(first, null)
            .ShouldBe(new[] { first });
    }

    [Fact]
    public void 行锁Provider必须只允许PostgreSQL与SQLite测试接缝()
    {
        EfCoreDataPipelineRepository.ResolveActivationLockProvider(
                "Npgsql.EntityFrameworkCore.PostgreSQL")
            .ShouldBe(EfCoreDataPipelineRepository.ActivationLockProvider
                .PostgreSql);
        EfCoreDataPipelineRepository.ResolveActivationLockProvider(
                "Microsoft.EntityFrameworkCore.Sqlite")
            .ShouldBe(EfCoreDataPipelineRepository.ActivationLockProvider
                .SqliteTestSeam);

        Should.Throw<InvalidOperationException>(() =>
            EfCoreDataPipelineRepository.ResolveActivationLockProvider(
                "Unexpected.Provider"));
    }

    [Fact]
    public async Task SQLite接缝应在同一UoW返回管道与源目标三实体()
    {
        var seeded = await CreateReceiptPipelineAsync();

        await WithUnitOfWorkAsync(async () =>
        {
            var locked = await _pipelineRepository
                .GetActivationForUpdateAsync(seeded.Pipeline.Id);

            locked.Pipeline.Id.ShouldBe(seeded.Pipeline.Id);
            locked.Source.Id.ShouldBe(seeded.Source.Id);
            locked.Target.ShouldNotBeNull();
            locked.Target!.Id.ShouldBe(seeded.Target.Id);
        });
    }

    [Fact]
    public async Task receipt目标探针期间不得存在平台UoW()
    {
        var seeded = await CreateReceiptPipelineAsync();
        _receiptStore.ObserveProbeAmbientUnitOfWork(
            () => _unitOfWorkManager.Current != null);

        await _dataPipelineAppService.ActivateAsync(seeded.Pipeline.Id);

        _receiptStore.ProbeCalls.ShouldBe(1);
        _receiptStore.LastProbeHadAmbientUnitOfWork.ShouldBe(
            false,
            "外部目标探针期间不能持有平台 UoW/事务");
        (await _dataPipelineAppService.GetAsync(seeded.Pipeline.Id))
            .Status.ShouldBe(PipelineStatus.Active);
    }

    [Fact]
    public async Task ambient_UoW调用必须在目标探针前失败关闭()
    {
        var seeded = await CreateReceiptPipelineAsync();

        await WithUnitOfWorkAsync(async () =>
        {
            await Should.ThrowAsync<InvalidOperationException>(() =>
                _dataPipelineAppService.ActivateAsync(seeded.Pipeline.Id));
        });

        _receiptStore.ProbeCalls.ShouldBe(0);
        (await _dataPipelineAppService.GetAsync(seeded.Pipeline.Id))
            .Status.ShouldBe(PipelineStatus.Draft);
    }

    [Fact]
    public async Task topology_pin应贯穿探针与两段UoW并在结束后恢复()
    {
        const string capturedDefault = "sqlite:pinned-before-probe";
        const string changedDefault = "sqlite:changed-during-probe";
        var seeded = await CreateReceiptPipelineAsync();
        _topologyGuard.Reset();
        _topologyGuard.ConfiguredDefault = capturedDefault;
        string? pinnedDuringProbe = null;
        PipelineTerminalStoreTopologyPhase[]? callsDuringProbe = null;
        _receiptStore.ProbeCallback = () =>
        {
            pinnedDuringProbe = _topologyGuard.CurrentPinnedDefault;
            callsDuringProbe = _topologyGuard.Calls.ToArray();
            _topologyGuard.ConfiguredDefault = changedDefault;
        };

        await _dataPipelineAppService.ActivateAsync(seeded.Pipeline.Id);

        var phase = PipelineTerminalStoreTopologyPhase
            .TargetReceiptActivation;
        callsDuringProbe.ShouldBe(new[] { phase, phase, phase },
            "Capture、Enter 与第一段 UoW 的 Ensure 必须均早于外部 probe");
        _topologyGuard.Calls.ShouldBe(
            new[] { phase, phase, phase, phase },
            "第二段提交 UoW 必须再次 Ensure 同一 activation scope");
        _topologyGuard.SafeScopeDefaults.ShouldBe(
            new[] { capturedDefault, capturedDefault },
            "probe 中途热变不得让第二段 UoW 漂移到新 Default");
        pinnedDuringProbe.ShouldBe(capturedDefault);
        _topologyGuard.ConfiguredDefault.ShouldBe(changedDefault);
        _topologyGuard.IsScopeActive.ShouldBeFalse(
            "activation 调用结束必须撤销 topology scope");
        _topologyGuard.CurrentPinnedDefault.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task receipt激活后源或目标配置漂移必须在争Fence前失败关闭(
        bool mutateTarget)
    {
        var seeded = await CreateReceiptPipelineAsync();
        await _dataPipelineAppService.ActivateAsync(seeded.Pipeline.Id);
        var original = mutateTarget ? seeded.Target : seeded.Source;
        var changedConfig = JsonNode.Parse(original.Config)!.AsObject();
        changedConfig["username"] = $"reader_{Guid.NewGuid():N}";
        changedConfig["password"] = "TEST_ONLY_ACTIVATION_PASSWORD";
        await _dataSourceAppService.UpdateAsync(
            original.Id,
            new DataSourceCreateUpdateDto
            {
                Name = original.Name + " drift",
                Description = original.Description,
                Type = original.Type,
                Config = changedConfig.ToJsonString(),
                IsEnabled = true,
            });
        var runId = Guid.NewGuid();

        var error = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.ExecuteAsync(
                seeded.Pipeline.Id,
                runId));

        error.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineTargetReceiptInvalid);
        _executionFence.AcquisitionCalls.ShouldBe(0,
            "配置漂移必须在 PostgreSQL fence 前由激活合同拒绝");
        _etlRunner.RunCalls.ShouldBe(0,
            "配置漂移不得启动任何源库或目标库工作");
        (await _executionRepository.FindAsync(runId)).ShouldBeNull(
            "失败关闭不得留下可被误当成已执行的 Running 记录");
    }

    [Fact]
    public void 激活协调器DI应完整解析()
    {
        GetRequiredService<DataPipelineActivationCoordinator>()
            .ShouldNotBeNull();
    }

    private async Task<ActivationSeed> CreateReceiptPipelineAsync()
    {
        var source = await CreateDataSourceAsync("source");
        var target = await CreateDataSourceAsync("target");
        var pipeline = await _dataPipelineAppService.CreateAsync(
            new DataPipelineCreateUpdateDto
            {
                Name = $"Activation lock {Guid.NewGuid():N}",
                SourceId = source.Id,
                TargetId = target.Id,
                TransformConfig = JsonSerializer.Serialize(new
                {
                    sourceQuery = "SELECT 1 AS Id",
                    targetTable = "dbo.Target",
                    mode = "append-receipt-v1",
                }),
            });
        return new ActivationSeed(pipeline, source, target);
    }

    private Task<DataSourceDto> CreateDataSourceAsync(string kind)
        => _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"Activation {kind} {Guid.NewGuid():N}",
            Type = DataSourceType.RelationalDatabase,
            Config = JsonSerializer.Serialize(new
            {
                provider = "sqlserver",
                host = "localhost",
                port = 1433,
                database = "Han",
                username = "reader",
                targetResourceGroup =
                    $"test:activation-{Guid.NewGuid():N}",
            }),
        });

    private sealed record ActivationSeed(
        DataPipelineDto Pipeline,
        DataSourceDto Source,
        DataSourceDto Target);
}
