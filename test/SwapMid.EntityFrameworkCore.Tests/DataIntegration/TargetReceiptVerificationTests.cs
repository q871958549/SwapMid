using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using SwapMid.Monitoring;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace SwapMid.DataIntegration;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public sealed class TargetReceiptVerificationTests
    : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IDataSourceAppService _dataSourceAppService;
    private readonly IDataPipelineAppService _dataPipelineAppService;
    private readonly IPipelineExecutionAppService _executionAppService;
    private readonly DataPipelineExecutionCoordinator _executionCoordinator;
    private readonly PipelineExecutionFailurePolicy _failurePolicy;
    private readonly IRepository<DataSource, Guid> _dataSourceRepository;
    private readonly IRepository<PipelineExecution, Guid> _executionRepository;
    private readonly FakeEtlPipelineRunner _etlRunner;
    private readonly FakeEtlTargetReceiptStore _receiptStore;
    private readonly TestDataPipelineExecutionFence _fence;
    private readonly TestPipelineTerminalStoreTopologyGuard _topologyGuard;
    private readonly ICurrentTenant _currentTenant;
    private readonly IBackgroundJobStore _backgroundJobStore;
    private readonly IBackgroundJobSerializer _backgroundJobSerializer;
    private readonly AbpBackgroundJobWorkerOptions _backgroundJobWorkerOptions;

    public TargetReceiptVerificationTests()
    {
        _dataSourceAppService = GetRequiredService<IDataSourceAppService>();
        _dataPipelineAppService = GetRequiredService<IDataPipelineAppService>();
        _executionAppService =
            GetRequiredService<IPipelineExecutionAppService>();
        _executionCoordinator = GetRequiredService<
            DataPipelineExecutionCoordinator>();
        _failurePolicy = GetRequiredService<
            PipelineExecutionFailurePolicy>();
        _dataSourceRepository =
            GetRequiredService<IRepository<DataSource, Guid>>();
        _executionRepository =
            GetRequiredService<IRepository<PipelineExecution, Guid>>();
        _etlRunner = (FakeEtlPipelineRunner)GetRequiredService<
            IEtlPipelineRunner>();
        _receiptStore = GetRequiredService<FakeEtlTargetReceiptStore>();
        _fence = GetRequiredService<TestDataPipelineExecutionFence>();
        _topologyGuard = GetRequiredService<
            TestPipelineTerminalStoreTopologyGuard>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _backgroundJobStore = GetRequiredService<IBackgroundJobStore>();
        _backgroundJobSerializer =
            GetRequiredService<IBackgroundJobSerializer>();
        _backgroundJobWorkerOptions = GetRequiredService<
            IOptions<AbpBackgroundJobWorkerOptions>>().Value;
        _etlRunner.Reset();
        _receiptStore.Reset();
        _fence.Reset();
        _topologyGuard.Reset();
    }

    [Fact]
    public async Task 已禁用目标的历史Receipt精确回执仍应允许机器核验收敛()
    {
        var seeded = await SeedReceiptExecutionAsync(
            ExecutionStatus.OutcomeUnknown);
        await DisableDataSourceAsync(seeded.TargetId);
        (await LoadDataSourceAsync(seeded.TargetId)).IsEnabled.ShouldBeFalse();

        var result = await _executionAppService.VerifyTargetReceiptAsync(
            seeded.Execution.Id);

        result.Status.ShouldBe(ExecutionStatus.Succeeded);
        result.ProcessedRows.ShouldBe(
            FakeEtlTargetReceiptStore.DefaultProcessedRows);
        _receiptStore.FindCalls.ShouldBe(1);
        AssertDualTargetFence(seeded);
        var stored = await LoadExecutionAsync(seeded.Execution.Id);
        AssertCompleteMachineVerificationMarker(stored);
    }

    [Fact]
    public async Task 已禁用目标的历史Receipt缺失仍应允许破玻璃收敛()
    {
        var seeded = await SeedReceiptExecutionAsync(
            ExecutionStatus.OutcomeUnknown);
        await DisableDataSourceAsync(seeded.TargetId);
        (await LoadDataSourceAsync(seeded.TargetId)).IsEnabled.ShouldBeFalse();
        _receiptStore.Behavior = FakeReceiptBehavior.Absent;

        var result = await _executionAppService.ResolveOutcomeAsync(
            seeded.Execution.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution
                    .ConfirmedNotApplied,
                VerificationNote =
                    "dba-evidence:DISABLED-TARGET-ABSENT-0001",
            });

        result.Status.ShouldBe(ExecutionStatus.Failed);
        result.ErrorCode.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReconciledNotApplied);
        _receiptStore.FindCalls.ShouldBe(0);
        _receiptStore.ReadFenceAcquireCalls.ShouldBe(1);
        _receiptStore.ReadFenceDisposeCalls.ShouldBe(1);
        _receiptStore.ActiveReadFences.ShouldBe(0);
        AssertDualTargetFence(seeded);
        var stored = await LoadExecutionAsync(seeded.Execution.Id);
        ReadExtraString(
                stored,
                DataPipelineExecutionCoordinator
                    .ReceiptManualOverrideSourcePropertyName)
            .ShouldBe("target-receipt-v1-absent-break-glass");
    }

    [Fact]
    public async Task 已禁用目标仍必须在FenceRunner和外连前阻止普通执行()
    {
        var (pipeline, target) = await CreatePipelineAsync("append");
        await _dataPipelineAppService.ActivateAsync(pipeline.Id);
        await DisableDataSourceAsync(target.Id);
        (await LoadDataSourceAsync(target.Id)).IsEnabled.ShouldBeFalse();
        _etlRunner.Reset();
        _receiptStore.Reset();
        _fence.Reset();
        _topologyGuard.Reset();
        var runId = Guid.NewGuid();

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.ExecuteAsync(pipeline.Id, runId));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionInputInvalid);
        _fence.AcquisitionCalls.ShouldBe(0);
        _etlRunner.RunCalls.ShouldBe(0);
        _receiptStore.ProbeCalls.ShouldBe(0);
        _receiptStore.FindCalls.ShouldBe(0);
        _receiptStore.ReadFenceAcquireCalls.ShouldBe(0);
        (await WithUnitOfWorkAsync(() =>
                _executionRepository.FindAsync(runId)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task 完整匹配应收敛成功回填证据且幂等回放不再外连或重复指标()
    {
        var seeded = await SeedReceiptExecutionAsync(
            ExecutionStatus.OutcomeUnknown);
        var beforeVerification = await _executionAppService.GetAsync(
            seeded.Execution.Id);
        beforeVerification.ReconciliationStrategy.ShouldBe(
            "target-receipt-v1");
        beforeVerification.ReceiptVerificationEligible.ShouldBeTrue();
        beforeVerification.ReceiptVerifiedAt.ShouldBeNull();
        beforeVerification.ReceiptVerificationSource.ShouldBeNull();
        var verifiedEvents = PlatformMetrics
            .EtlExecutionCoordinationEventsTotal
            .WithLabels("target_receipt_verified");
        var verifiedBefore = verifiedEvents.Value;

        var result = await _executionAppService.VerifyTargetReceiptAsync(
            seeded.Execution.Id);

        result.Status.ShouldBe(ExecutionStatus.Succeeded);
        result.ProcessedRows.ShouldBe(
            FakeEtlTargetReceiptStore.DefaultProcessedRows);
        result.ReconciliationStrategy.ShouldBe("target-receipt-v1");
        result.ReceiptVerificationEligible.ShouldBeFalse();
        result.ReceiptVerificationSource.ShouldBe("target-receipt-v1");
        result.ReceiptVerifiedAt.ShouldNotBeNull();
        _receiptStore.FindCalls.ShouldBe(1);
        _fence.AcquisitionCalls.ShouldBe(1);
        _fence.LastTargetResources.Count.ShouldBe(2);
        _fence.LastTargetResources.ShouldBe(
            new[] { seeded.LegacyHash, seeded.VerifiedHash }
                .OrderBy(value => value, StringComparer.Ordinal));
        _topologyGuard.Calls.ShouldContain(
            PipelineTerminalStoreTopologyPhase.TargetReceiptVerification);
        verifiedEvents.Value.ShouldBe(verifiedBefore + 1);

        var stored = await LoadExecutionAsync(seeded.Execution.Id);
        stored.Status.ShouldBe(ExecutionStatus.Succeeded);
        stored.ProcessedRows.ShouldBe(
            FakeEtlTargetReceiptStore.DefaultProcessedRows);
        ReadExtraString(
                stored,
                DataPipelineExecutionCoordinator
                    .ReceiptTargetSchemaHashPropertyName)
            .ShouldBe(FakeEtlTargetReceiptStore.DefaultSchemaHash);
        ReadExtraString(
                stored,
                DataPipelineExecutionCoordinator
                    .ReceiptBatchDigestPropertyName)
            .ShouldBe(FakeEtlTargetReceiptStore.DefaultBatchDigest);
        ReadExtraLong(
                stored,
                DataPipelineExecutionCoordinator
                    .ReceiptPreparedRowsPropertyName)
            .ShouldBe(FakeEtlTargetReceiptStore.DefaultProcessedRows);
        ReadExtraString(
                stored,
                DataPipelineExecutionCoordinator.ResolutionPropertyName)
            .ShouldBe(
                DataPipelineExecutionCoordinator
                    .TargetReceiptVerifiedResolution);
        AssertCompleteMachineVerificationMarker(stored);

        var replay = await _executionAppService.VerifyTargetReceiptAsync(
            seeded.Execution.Id);

        replay.Status.ShouldBe(ExecutionStatus.Succeeded);
        replay.ReceiptVerifiedAt.ShouldBe(result.ReceiptVerifiedAt);
        _receiptStore.FindCalls.ShouldBe(1,
            "完整机器核验 marker 的幂等回放不得再次连接目标库");
        _fence.AcquisitionCalls.ShouldBe(1,
            "完整机器核验 marker 的幂等回放不得重复争锁");
        verifiedEvents.Value.ShouldBe(verifiedBefore + 1,
            "幂等回放不得重复记录 verified 事件");
    }

    [Fact]
    public async Task 缺失不匹配或不可达均应保持未知态()
    {
        var scenarios = new[]
        {
            (FakeReceiptBehavior.Absent,
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionReceiptInconclusive),
            (FakeReceiptBehavior.Mismatch,
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionReceiptInconclusive),
            (FakeReceiptBehavior.Unavailable,
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineTargetReceiptUnavailable),
        };

        foreach (var (behavior, expectedCode) in scenarios)
        {
            _receiptStore.Reset();
            _fence.Reset();
            _topologyGuard.Reset();
            var seeded = await SeedReceiptExecutionAsync(
                ExecutionStatus.OutcomeUnknown);
            _receiptStore.Behavior = behavior;

            var exception = await Should.ThrowAsync<BusinessException>(() =>
                _executionAppService.VerifyTargetReceiptAsync(
                    seeded.Execution.Id));

            exception.Code.ShouldBe(expectedCode);
            _receiptStore.FindCalls.ShouldBe(1);
            (await LoadExecutionAsync(seeded.Execution.Id)).Status.ShouldBe(
                ExecutionStatus.OutcomeUnknown,
                "目标库没有提供完整肯定证据时，平台不得猜测 Applied/NotApplied");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparedEvidence仅Batch或Rows不同时核验必须ETL027且保持隔离(
        bool mismatchOnlyByRows)
    {
        var seeded = await SeedReceiptExecutionAsync(
            ExecutionStatus.Running);
        var context = CreateReceiptContext(seeded.Execution);
        var prepared = FakeEtlTargetReceiptStore
            .CreateMatchingEvidence(context) with
        {
            BatchDigest = mismatchOnlyByRows
                ? FakeEtlTargetReceiptStore.DefaultBatchDigest
                : new string(
                    'E',
                    PipelineExecutionConsts.Sha256HexLength),
            ProcessedRows = mismatchOnlyByRows
                ? FakeEtlTargetReceiptStore.DefaultProcessedRows + 1
                : FakeEtlTargetReceiptStore.DefaultProcessedRows,
        };

        var unknown = await InvokeFinalizeOutcomeUnknownAsync(
            seeded.Execution.Id,
            prepared);

        unknown.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        var stored = await LoadExecutionAsync(seeded.Execution.Id);
        ReadExtraString(
                stored,
                DataPipelineExecutionCoordinator
                    .ReceiptTargetSchemaHashPropertyName)
            .ShouldBe(prepared.TargetSchemaHash);
        ReadExtraString(
                stored,
                DataPipelineExecutionCoordinator
                    .ReceiptBatchDigestPropertyName)
            .ShouldBe(prepared.BatchDigest);
        ReadExtraLong(
                stored,
                DataPipelineExecutionCoordinator
                    .ReceiptPreparedRowsPropertyName)
            .ShouldBe(prepared.ProcessedRows);

        _receiptStore.Behavior = FakeReceiptBehavior.Match;
        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _executionAppService.VerifyTargetReceiptAsync(
                seeded.Execution.Id));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReceiptInconclusive);
        _receiptStore.FindCalls.ShouldBe(1);
        var afterVerify = await LoadExecutionAsync(seeded.Execution.Id);
        afterVerify.Status.ShouldBe(
            ExecutionStatus.OutcomeUnknown,
            "ETL:027 不得释放 receipt-v1 quarantine");
        (await _executionAppService.GetAsync(afterVerify.Id))
            .ReceiptVerificationEligible.ShouldBeTrue(
                "不匹配后仍应保留机器核验资格与 durable quarantine");
        ReadExtraString(
                afterVerify,
                DataPipelineExecutionCoordinator
                    .ReceiptBatchDigestPropertyName)
            .ShouldBe(prepared.BatchDigest);
        ReadExtraLong(
                afterVerify,
                DataPipelineExecutionCoordinator
                    .ReceiptPreparedRowsPropertyName)
            .ShouldBe(prepared.ProcessedRows);
    }

    [Fact]
    public async Task Running与ManualV0均应在外连及争锁前拒绝()
    {
        var running = await SeedReceiptExecutionAsync(
            ExecutionStatus.Running);

        var runningError = await Should.ThrowAsync<BusinessException>(() =>
            _executionAppService.VerifyTargetReceiptAsync(
                running.Execution.Id));
        runningError.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReceiptInconclusive);

        var manual = await SeedManualUnknownExecutionAsync();
        var manualError = await Should.ThrowAsync<BusinessException>(() =>
            _executionAppService.VerifyTargetReceiptAsync(manual.Id));
        manualError.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReceiptInconclusive);

        _receiptStore.FindCalls.ShouldBe(0);
        _fence.AcquisitionCalls.ShouldBe(0);
        (await LoadExecutionAsync(running.Execution.Id)).Status.ShouldBe(
            ExecutionStatus.Running);
        (await LoadExecutionAsync(manual.Id)).Status.ShouldBe(
            ExecutionStatus.OutcomeUnknown);
    }

    [Fact]
    public async Task Tenant入口必须在锁与外连前拒绝并恢复调用方租户()
    {
        var seeded = await SeedReceiptExecutionAsync(
            ExecutionStatus.OutcomeUnknown);
        var tenantId = Guid.NewGuid();
        var originalTenantId = _currentTenant.Id;

        using (_currentTenant.Change(tenantId, "receipt-verifier-tenant"))
        {
            var exception = await Should.ThrowAsync<AbpAuthorizationException>(
                () => _executionAppService.VerifyTargetReceiptAsync(
                    seeded.Execution.Id));
            exception.Code.ShouldBe(
                SwapMid.Permissions
                    .SwapMidPermissions.DataIntegration
                    .PipelineExecutions.VerifyTargetReceipt);
            _currentTenant.Id.ShouldBe(tenantId);
        }

        _currentTenant.Id.ShouldBe(originalTenantId);
        _receiptStore.FindCalls.ShouldBe(0);
        _fence.AcquisitionCalls.ShouldBe(0);
        (await LoadExecutionAsync(seeded.Execution.Id)).Status.ShouldBe(
            ExecutionStatus.OutcomeUnknown);
    }

    [Fact]
    public async Task 拓扑pin拒绝必须在目标外连与栅栏前失败关闭()
    {
        var seeded = await SeedReceiptExecutionAsync(
            ExecutionStatus.OutcomeUnknown);
        _topologyGuard.Reset();
        _topologyGuard.RejectAt(
            PipelineTerminalStoreTopologyPhase.TargetReceiptVerification);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _executionAppService.VerifyTargetReceiptAsync(
                seeded.Execution.Id));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        _topologyGuard.Calls.ShouldContain(
            PipelineTerminalStoreTopologyPhase.TargetReceiptVerification);
        _receiptStore.FindCalls.ShouldBe(0);
        _fence.AcquisitionCalls.ShouldBe(0);
        (await LoadExecutionAsync(seeded.Execution.Id)).Status.ShouldBe(
            ExecutionStatus.OutcomeUnknown);
    }

    [Fact]
    public async Task ReceiptV1人工已应用必须在外连与双目标栅栏前拒绝()
    {
        var seeded = await SeedReceiptExecutionAsync(
            ExecutionStatus.OutcomeUnknown);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _executionAppService.ResolveOutcomeAsync(
                seeded.Execution.Id,
                new ResolvePipelineExecutionOutcomeInput
                {
                    Resolution =
                        PipelineExecutionResolution.ConfirmedApplied,
                    ProcessedRows = 12,
                    VerificationNote =
                        "incident:RECEIPT-MANUAL-APPLIED-0001",
                }));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReceiptVerificationRequired);
        _receiptStore.FindCalls.ShouldBe(0,
            "receipt-v1 人工已应用不得连接目标库猜测结果");
        _fence.AcquisitionCalls.ShouldBe(0,
            "receipt-v1 人工已应用应在争双目标 fence 前拒绝");
        _topologyGuard.Calls.ShouldBeEmpty(
            "人工已应用不应进入失败通知拓扑 pin");
        var stored = await LoadExecutionAsync(seeded.Execution.Id);
        stored.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        stored.ExtraProperties.ContainsKey(
                DataPipelineExecutionCoordinator.ResolutionPropertyName)
            .ShouldBeFalse();
        (await LoadNotificationJobsAsync(seeded.Execution.Id))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task ReceiptV1人工未应用不得使用旧自由文本兼容占位绕过结构化证据()
    {
        var seeded = await SeedReceiptExecutionAsync(
            ExecutionStatus.OutcomeUnknown);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _executionAppService.ResolveOutcomeAsync(
                seeded.Execution.Id,
                new ResolvePipelineExecutionOutcomeInput
                {
                    Resolution = PipelineExecutionResolution
                        .ConfirmedNotApplied,
                    VerificationNote =
                        "legacy operator note Password=secret",
                }));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionResolutionInvalid);
        _receiptStore.FindCalls.ShouldBe(0);
        _receiptStore.ReadFenceAcquireCalls.ShouldBe(0);
        _fence.AcquisitionCalls.ShouldBe(0);
        _topologyGuard.Calls.ShouldBeEmpty();
        var stored = await LoadExecutionAsync(seeded.Execution.Id);
        stored.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        stored.ExtraProperties.ContainsKey(
                DataPipelineExecutionCoordinator.VerificationNotePropertyName)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task ReceiptV1人工未应用只有回执缺失才可收敛失败()
    {
        var scenarios = new[]
        {
            (FakeReceiptBehavior.Match,
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionReceiptVerificationRequired),
            (FakeReceiptBehavior.Mismatch,
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionReceiptInconclusive),
            (FakeReceiptBehavior.Unavailable,
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineTargetReceiptUnavailable),
        };

        foreach (var (behavior, expectedCode) in scenarios)
        {
            _receiptStore.Reset();
            _fence.Reset();
            _topologyGuard.Reset();
            var seeded = await SeedReceiptExecutionAsync(
                ExecutionStatus.OutcomeUnknown);
            _receiptStore.Behavior = behavior;

            var exception = await Should.ThrowAsync<BusinessException>(() =>
                _executionAppService.ResolveOutcomeAsync(
                    seeded.Execution.Id,
                    new ResolvePipelineExecutionOutcomeInput
                    {
                        Resolution = PipelineExecutionResolution
                            .ConfirmedNotApplied,
                        VerificationNote =
                            $"incident:RECEIPT-NOT-APPLIED-{behavior}-0001",
                    }));

            exception.Code.ShouldBe(expectedCode);
            _receiptStore.FindCalls.ShouldBe(0,
                "人工未应用必须使用持锁读取 seam，不能退回无锁 FindAsync");
            _receiptStore.ReadFenceAcquireCalls.ShouldBe(1);
            _receiptStore.ReadFenceDisposeCalls.ShouldBe(
                behavior == FakeReceiptBehavior.Unavailable ? 0 : 1);
            _receiptStore.ActiveReadFences.ShouldBe(0);
            AssertDualTargetFence(seeded);
            _topologyGuard.Calls.ShouldContain(
                PipelineTerminalStoreTopologyPhase.OutcomeResolution);
            var stored = await LoadExecutionAsync(seeded.Execution.Id);
            stored.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
            stored.ExtraProperties.ContainsKey(
                    DataPipelineExecutionCoordinator.ResolutionPropertyName)
                .ShouldBeFalse();
            (await LoadNotificationJobsAsync(seeded.Execution.Id))
                .ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task ReceiptV1回执缺失应写结构化人工审计且同结论幂等不再外连或增任务()
    {
        var seeded = await SeedReceiptExecutionAsync(
            ExecutionStatus.OutcomeUnknown);
        _receiptStore.Behavior = FakeReceiptBehavior.Absent;
        const string verificationNote =
            "dba-evidence:RECEIPT-ABSENCE-VERIFIED-0001";
        ExecutionStatus? statusAtReadFenceDispose = null;
        _receiptStore.ObserveReadFenceDispose(async () =>
        {
            statusAtReadFenceDispose =
                (await LoadExecutionAsync(seeded.Execution.Id)).Status;
        });

        var resolved = await _executionAppService.ResolveOutcomeAsync(
            seeded.Execution.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution
                    .ConfirmedNotApplied,
                VerificationNote = verificationNote,
            });

        resolved.Status.ShouldBe(ExecutionStatus.Failed);
        resolved.ProcessedRows.ShouldBeNull();
        resolved.ErrorCode.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReconciledNotApplied);
        _receiptStore.FindCalls.ShouldBe(0,
            "人工未应用必须使用持锁读取 seam，不能退回无锁 FindAsync");
        _receiptStore.ReadFenceAcquireCalls.ShouldBe(1);
        _receiptStore.ReadFenceDisposeCalls.ShouldBe(1);
        _receiptStore.ActiveReadFences.ShouldBe(0);
        statusAtReadFenceDispose.ShouldBe(ExecutionStatus.Failed,
            "目标端读 fence 必须保持到平台失败终态与通知任务同事务提交完成");
        AssertDualTargetFence(seeded);

        var stored = await LoadExecutionAsync(seeded.Execution.Id);
        ReadExtraString(
                stored,
                DataPipelineExecutionCoordinator.ResolutionPropertyName)
            .ShouldBe(
                PipelineExecutionResolution.ConfirmedNotApplied.ToString());
        ReadExtraString(
                stored,
                DataPipelineExecutionCoordinator.VerificationNotePropertyName)
            .ShouldBe(verificationNote);
        Guid.TryParseExact(
                ReadExtraString(
                    stored,
                    DataPipelineExecutionCoordinator.ResolvedByPropertyName),
                "N",
                out var resolverId)
            .ShouldBeTrue();
        resolverId.ShouldNotBe(Guid.Empty);
        ReadExtraString(
                stored,
                DataPipelineExecutionCoordinator
                    .ReceiptManualOverrideSourcePropertyName)
            .ShouldBe("target-receipt-v1-absent-break-glass");
        var resolvedAt = ReadExtraTimestamp(
            stored,
            DataPipelineExecutionCoordinator.ResolvedAtPropertyName);
        resolvedAt.ShouldNotBeNull();
        ReadExtraTimestamp(
                stored,
                DataPipelineExecutionCoordinator
                    .ReceiptAbsenceVerifiedAtPropertyName)
            .ShouldBe(resolvedAt);

        var queued = await LoadNotificationJobsAsync(seeded.Execution.Id);
        var notification = queued.ShouldHaveSingleItem();
        notification.PipelineId.ShouldBe(seeded.Execution.PipelineId);
        notification.ErrorCode.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReconciledNotApplied);
        notification.OutcomeUnknown.ShouldBeFalse();
        var fenceCalls = _fence.AcquisitionCalls;

        var replay = await _executionAppService.ResolveOutcomeAsync(
            seeded.Execution.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution
                    .ConfirmedNotApplied,
                VerificationNote =
                    "incident:RECEIPT-ABSENCE-IDEMPOTENT-0002",
            });

        replay.Status.ShouldBe(ExecutionStatus.Failed);
        _receiptStore.ReadFenceAcquireCalls.ShouldBe(1,
            "完整人工未应用 marker 的同结论回放不得再次连接目标库");
        _receiptStore.ReadFenceDisposeCalls.ShouldBe(1);
        _fence.AcquisitionCalls.ShouldBe(fenceCalls,
            "完整人工未应用 marker 的回放应在争锁前返回");
        (await LoadNotificationJobsAsync(seeded.Execution.Id)).Count.ShouldBe(
            1,
            "同结论回放不得重复创建失败通知任务");
        var replayStored = await LoadExecutionAsync(seeded.Execution.Id);
        ReadExtraString(
                replayStored,
                DataPipelineExecutionCoordinator.VerificationNotePropertyName)
            .ShouldBe(verificationNote,
                "幂等回放不得覆盖首次结构化证据引用");
    }

    [Fact]
    public async Task 人工未应用marker删除或损坏后裁决与恢复回放都必须失败关闭()
    {
        var seeded = await SeedReceiptExecutionAsync(
            ExecutionStatus.Running,
            startedAt: DateTime.UtcNow.AddMinutes(-12));
        var now = DateTimeOffset.UtcNow;
        var recovery = new RecoverPipelineExecutionInput
        {
            RecoveryRequestId = Guid.NewGuid(),
            ExpectedConcurrencyStamp = seeded.Execution.ConcurrencyStamp,
            DrainStartedAt = now.AddMinutes(-10),
            OldOwnerExitVerifiedAt = now.AddMinutes(-9),
            TargetSessionExitVerifiedAt = now.AddMinutes(-8),
            EvidenceReference = "incident:RECEIPT-MANUAL-RECOVERY-0001",
        };
        await _executionAppService.RecoverRunningAsync(
            seeded.Execution.Id,
            recovery);
        _receiptStore.Behavior = FakeReceiptBehavior.Absent;
        await _executionAppService.ResolveOutcomeAsync(
            seeded.Execution.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution
                    .ConfirmedNotApplied,
                VerificationNote =
                    "dba-evidence:RECEIPT-MANUAL-ABSENT-0001",
            });

        var complete = await LoadExecutionAsync(seeded.Execution.Id);
        var markerNames = new[]
        {
            DataPipelineExecutionCoordinator
                .ReceiptManualOverrideSourcePropertyName,
            DataPipelineExecutionCoordinator
                .ReceiptAbsenceVerifiedAtPropertyName,
        };
        var originalValues = markerNames.ToDictionary(
            propertyName => propertyName,
            propertyName => complete.ExtraProperties[propertyName]);
        var fenceCalls = _fence.AcquisitionCalls;
        var readFenceAcquireCalls = _receiptStore.ReadFenceAcquireCalls;
        var readFenceDisposeCalls = _receiptStore.ReadFenceDisposeCalls;
        var jobCount = (await LoadNotificationJobsAsync(
            seeded.Execution.Id)).Count;

        foreach (var propertyName in markerNames)
        {
            foreach (var remove in new[] { true, false })
            {
                await WithUnitOfWorkAsync(async () =>
                {
                    var tracked = await _executionRepository.GetAsync(
                        seeded.Execution.Id);
                    if (remove)
                    {
                        tracked.ExtraProperties.Remove(propertyName)
                            .ShouldBeTrue();
                    }
                    else
                    {
                        tracked.SetProperty(
                            propertyName,
                            propertyName == DataPipelineExecutionCoordinator
                                .ReceiptManualOverrideSourcePropertyName
                                ? "target-receipt-v1-corrupt"
                                : "not-a-timestamp");
                    }

                    await _executionRepository.UpdateAsync(
                        tracked,
                        autoSave: true);
                });

                var resolutionReplay = await Should.ThrowAsync<
                    BusinessException>(() =>
                    _executionAppService.ResolveOutcomeAsync(
                        seeded.Execution.Id,
                        new ResolvePipelineExecutionOutcomeInput
                        {
                            Resolution = PipelineExecutionResolution
                                .ConfirmedNotApplied,
                            VerificationNote =
                                "incident:RECEIPT-MANUAL-REPLAY-0002",
                        }));
                resolutionReplay.Code.ShouldBe(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionResolutionInvalid);

                var recoveryReplay = await Should.ThrowAsync<BusinessException>(
                    () => _executionAppService.RecoverRunningAsync(
                        seeded.Execution.Id,
                        recovery));
                recoveryReplay.Code.ShouldBe(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionCoordinationUnavailable);
                _receiptStore.FindCalls.ShouldBe(0);
                _receiptStore.ReadFenceAcquireCalls.ShouldBe(
                    readFenceAcquireCalls);
                _receiptStore.ReadFenceDisposeCalls.ShouldBe(
                    readFenceDisposeCalls);
                _receiptStore.ActiveReadFences.ShouldBe(0);
                _fence.AcquisitionCalls.ShouldBe(fenceCalls);
                (await LoadNotificationJobsAsync(seeded.Execution.Id)).Count
                    .ShouldBe(jobCount);
                (await LoadExecutionAsync(seeded.Execution.Id)).Status
                    .ShouldBe(ExecutionStatus.Failed);

                await WithUnitOfWorkAsync(async () =>
                {
                    var tracked = await _executionRepository.GetAsync(
                        seeded.Execution.Id);
                    tracked.SetProperty(
                        propertyName,
                        originalValues[propertyName]);
                    await _executionRepository.UpdateAsync(
                        tracked,
                        autoSave: true);
                });
            }
        }
    }

    [Fact]
    public async Task SQLServer双段128字符目标表必须完整保存并映射到DTO()
    {
        var targetTable =
            new string('S', 128) + "." + new string('T', 128);
        targetTable.Length.ShouldBe(
            PipelineExecutionConsts.MaxCanonicalTargetTableLength);
        var (pipeline, _) = await CreatePipelineAsync(
            "append",
            targetTable);
        await _dataPipelineAppService.ActivateAsync(pipeline.Id);

        var executed = await _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            Guid.NewGuid());
        var roundTripped = await _executionAppService.GetAsync(executed.Id);
        var stored = await LoadExecutionAsync(executed.Id);

        executed.Status.ShouldBe(ExecutionStatus.Succeeded);
        ReadExtraString(
                stored,
                DataPipelineExecutionCoordinator.TargetTablePropertyName)
            .ShouldBe(targetTable);
        executed.TargetTable.ShouldBe(targetTable);
        roundTripped.TargetTable.ShouldBe(targetTable,
            "DTO 映射不得把合法的 128+128 SQL Server 二段名当作越界值清空");
    }

    [Fact]
    public async Task 受控恢复marker后的机器核验应形成可幂等识别的合法终态()
    {
        var seeded = await SeedReceiptExecutionAsync(
            ExecutionStatus.Running,
            startedAt: DateTime.UtcNow.AddMinutes(-12));
        var now = DateTimeOffset.UtcNow;
        var recovery = new RecoverPipelineExecutionInput
        {
            RecoveryRequestId = Guid.NewGuid(),
            ExpectedConcurrencyStamp = seeded.Execution.ConcurrencyStamp,
            DrainStartedAt = now.AddMinutes(-10),
            OldOwnerExitVerifiedAt = now.AddMinutes(-9),
            TargetSessionExitVerifiedAt = now.AddMinutes(-8),
            EvidenceReference = "incident:RECEIPT-RECOVERY-0001",
        };

        var recovered = await _executionAppService.RecoverRunningAsync(
            seeded.Execution.Id,
            recovery);
        recovered.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);

        var verified = await _executionAppService.VerifyTargetReceiptAsync(
            seeded.Execution.Id);
        verified.Status.ShouldBe(ExecutionStatus.Succeeded);
        verified.ReceiptVerificationSource.ShouldBe("target-receipt-v1");
        var callsAfterVerification = _fence.AcquisitionCalls;

        var recoveryReplay = await _executionAppService.RecoverRunningAsync(
            seeded.Execution.Id,
            recovery);

        recoveryReplay.Status.ShouldBe(ExecutionStatus.Succeeded,
            "恢复完整性校验必须接受带 TargetReceiptVerifiedV1 的成功终态");
        recoveryReplay.ReceiptVerifiedAt.ShouldBe(verified.ReceiptVerifiedAt);
        _fence.AcquisitionCalls.ShouldBe(callsAfterVerification,
            "完整恢复 marker 的回放必须在争锁前返回");
        _receiptStore.FindCalls.ShouldBe(1);
    }

    private async Task<ReceiptSeed> SeedReceiptExecutionAsync(
        ExecutionStatus status,
        DateTime? startedAt = null)
    {
        var (pipeline, target) = await CreatePipelineAsync(
            "append-receipt-v1");
        var runId = Guid.NewGuid();
        var legacyHash = Sha256("legacy:" + runId.ToString("N"));
        var verifiedHash = Sha256("verified:" + runId.ToString("N"));
        var sourceQueryHash = Sha256("query:" + runId.ToString("N"));
        var execution = new PipelineExecution(
            runId,
            pipeline.Id,
            pipeline.Name,
            TriggerType.Manual,
            legacyHash,
            verifiedHash,
            evidenceProtocol: 1,
            targetResourceInstanceId: Guid.NewGuid(),
            executionContractHash: Sha256(
                "contract:" + runId.ToString("N")),
            targetCoordinationIdentityHash: Sha256(
                "coordination:" + runId.ToString("N")));
        SetStartedAt(execution, startedAt);
        StoreVerificationSnapshot(execution, target.Id, sourceQueryHash);
        if (status == ExecutionStatus.OutcomeUnknown)
        {
            execution.MarkOutcomeUnknown(DateTime.UtcNow);
        }
        else
        {
            status.ShouldBe(ExecutionStatus.Running);
        }

        await WithUnitOfWorkAsync(() =>
            _executionRepository.InsertAsync(execution, autoSave: true));
        return new ReceiptSeed(
            await LoadExecutionAsync(execution.Id),
            legacyHash,
            verifiedHash,
            target.Id);
    }

    private static EtlExecutionContext CreateReceiptContext(
        PipelineExecution execution)
    {
        var contextFactory = typeof(DataPipelineExecutionCoordinator)
            .GetMethod(
                "TryCreateReceiptContext",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "TryCreateReceiptContext test seam not found");
        var arguments = new object?[] { execution, null };
        ((bool)contextFactory.Invoke(null, arguments)!).ShouldBeTrue();
        return (EtlExecutionContext)arguments[1]!;
    }

    private async Task<PipelineExecution> InvokeFinalizeOutcomeUnknownAsync(
        Guid executionId,
        EtlTargetReceiptEvidence evidence)
    {
        var topologyPin = await _topologyGuard.CaptureSafeAsync(
            PipelineTerminalStoreTopologyPhase.TerminalFinalize);
        using var topologyScope = _topologyGuard.EnterSafeScope(
            topologyPin,
            PipelineTerminalStoreTopologyPhase.TerminalFinalize);
        var method = typeof(DataPipelineExecutionCoordinator).GetMethod(
                         "FinalizeCoreAsync",
                         BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException(
                         "FinalizeCoreAsync test seam not found");
        var terminalKind = Enum.Parse(
            method.GetParameters()[1].ParameterType,
            "OutcomeUnknown");
        var failure = _failurePolicy.DescribeOutcomeUnknown(
            new EtlTargetCommitOutcomeUnknownException(evidence),
            executionId);
        var task = method.Invoke(
                       _executionCoordinator,
                       new object?[]
                       {
                           executionId,
                           terminalKind,
                           null,
                           failure,
                           evidence,
                       }) as Task<PipelineExecution>
                   ?? throw new InvalidOperationException(
                       "FinalizeCoreAsync returned unexpected task type");
        return await task;
    }

    private async Task<PipelineExecution> SeedManualUnknownExecutionAsync()
    {
        var (pipeline, target) = await CreatePipelineAsync("append");
        var runId = Guid.NewGuid();
        var execution = new PipelineExecution(
            runId,
            pipeline.Id,
            pipeline.Name,
            TriggerType.Manual,
            Sha256("manual:" + runId.ToString("N")));
        StoreVerificationSnapshot(
            execution,
            target.Id,
            Sha256("query:" + runId.ToString("N")));
        execution.MarkOutcomeUnknown(DateTime.UtcNow);
        await WithUnitOfWorkAsync(() =>
            _executionRepository.InsertAsync(execution, autoSave: true));
        return await LoadExecutionAsync(execution.Id);
    }

    private async Task<(DataPipelineDto Pipeline, DataSourceDto Target)>
        CreatePipelineAsync(
            string mode,
            string targetTable = "dbo.Target")
    {
        var source = await CreateDataSourceAsync("source");
        var target = await CreateDataSourceAsync("target");
        var pipeline = await _dataPipelineAppService.CreateAsync(
            new DataPipelineCreateUpdateDto
            {
                Name = $"Receipt verifier {Guid.NewGuid():N}",
                SourceId = source.Id,
                TargetId = target.Id,
                TransformConfig = JsonSerializer.Serialize(new
                {
                    sourceQuery = "SELECT 1 AS Id",
                    targetTable,
                    mode,
                }),
            });
        return (pipeline, target);
    }

    private Task<DataSourceDto> CreateDataSourceAsync(string kind)
        => _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"Receipt {kind} {Guid.NewGuid():N}",
            Type = DataSourceType.RelationalDatabase,
            Config = JsonSerializer.Serialize(new
            {
                provider = "sqlserver",
                host = "localhost",
                port = 1433,
                database = "SwapMid",
                username = "reader",
                targetResourceGroup = $"test:receipt-{Guid.NewGuid():N}",
            }),
        });

    private static void StoreVerificationSnapshot(
        PipelineExecution execution,
        Guid targetId,
        string sourceQueryHash)
    {
        execution.SetProperty(
            DataPipelineExecutionCoordinator.TargetDataSourceIdPropertyName,
            targetId.ToString("N"));
        execution.SetProperty(
            DataPipelineExecutionCoordinator.TargetTablePropertyName,
            "dbo.Target");
        execution.SetProperty(
            DataPipelineExecutionCoordinator.WriteModePropertyName,
            "append");
        execution.SetProperty(
            DataPipelineExecutionCoordinator.SourceQuerySha256PropertyName,
            sourceQueryHash);
    }

    private static void SetStartedAt(
        PipelineExecution execution,
        DateTime? startedAt)
    {
        if (!startedAt.HasValue)
        {
            return;
        }

        typeof(PipelineExecution)
            .GetProperty(nameof(PipelineExecution.StartedAt))!
            .SetValue(execution, startedAt.Value);
    }

    private Task<PipelineExecution> LoadExecutionAsync(Guid id)
        => WithUnitOfWorkAsync(() => _executionRepository.GetAsync(id));

    private Task<DataSource> LoadDataSourceAsync(Guid id)
        => WithUnitOfWorkAsync(() => _dataSourceRepository.GetAsync(id));

    private Task DisableDataSourceAsync(Guid id)
        => WithUnitOfWorkAsync(async () =>
        {
            var dataSource = await _dataSourceRepository.GetAsync(id);
            dataSource.Disable();
            await _dataSourceRepository.UpdateAsync(
                dataSource,
                autoSave: true);
        });

    private async Task<System.Collections.Generic.List<
        PipelineFailureNotificationJobArgs>> LoadNotificationJobsAsync(
        Guid executionId)
    {
        var waitingJobs = await WithUnitOfWorkAsync(() =>
            _backgroundJobStore.GetWaitingJobsAsync(
                _backgroundJobWorkerOptions.ApplicationName,
                1024));
        return waitingJobs
            .Where(job => job.JobName ==
                BackgroundJobNameAttribute.GetName<
                    PipelineFailureNotificationJobArgs>())
            .Select(job => (PipelineFailureNotificationJobArgs)
                _backgroundJobSerializer.Deserialize(
                    job.JobArgs,
                    typeof(PipelineFailureNotificationJobArgs)))
            .Where(args => args.ExecutionId == executionId)
            .ToList();
    }

    private void AssertDualTargetFence(ReceiptSeed seeded)
    {
        _fence.AcquisitionCalls.ShouldBe(1);
        _fence.LastTargetResources.Count.ShouldBe(2);
        _fence.LastTargetResources.ShouldBe(
            new[] { seeded.LegacyHash, seeded.VerifiedHash }
                .OrderBy(value => value, StringComparer.Ordinal));
    }

    private static string Sha256(string value)
        => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string? ReadExtraString(
        PipelineExecution execution,
        string propertyName)
    {
        if (!execution.ExtraProperties.TryGetValue(propertyName, out var value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json =>
                json.GetString(),
            _ => null,
        };
    }

    private static long? ReadExtraLong(
        PipelineExecution execution,
        string propertyName)
    {
        if (!execution.ExtraProperties.TryGetValue(propertyName, out var value))
        {
            return null;
        }

        return value switch
        {
            long number => number,
            int number => number,
            JsonElement { ValueKind: JsonValueKind.Number } json
                when json.TryGetInt64(out var number) => number,
            _ => null,
        };
    }

    private static DateTimeOffset? ReadExtraTimestamp(
        PipelineExecution execution,
        string propertyName)
    {
        if (!execution.ExtraProperties.TryGetValue(propertyName, out var value))
        {
            return null;
        }

        return value switch
        {
            DateTimeOffset offset => offset.ToUniversalTime(),
            DateTime dateTime => dateTime.Kind switch
            {
                DateTimeKind.Utc => new DateTimeOffset(dateTime),
                DateTimeKind.Local => new DateTimeOffset(dateTime)
                    .ToUniversalTime(),
                _ => new DateTimeOffset(
                    DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            },
            string text when DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed) => parsed.ToUniversalTime(),
            JsonElement { ValueKind: JsonValueKind.String } json
                when DateTimeOffset.TryParse(
                    json.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed) => parsed.ToUniversalTime(),
            _ => null,
        };
    }

    private static void AssertCompleteMachineVerificationMarker(
        PipelineExecution execution)
    {
        ReadExtraString(
                execution,
                DataPipelineExecutionCoordinator
                    .ReceiptVerificationSourcePropertyName)
            .ShouldBe("target-receipt-v1");
        ReadExtraString(
                execution,
                DataPipelineExecutionCoordinator.VerificationNotePropertyName)
            .ShouldBe("target-receipt-v1");
        var resolvedAt = ReadExtraTimestamp(
            execution,
            DataPipelineExecutionCoordinator.ResolvedAtPropertyName);
        var verifiedAt = ReadExtraTimestamp(
            execution,
            DataPipelineExecutionCoordinator.ReceiptVerifiedAtPropertyName);
        resolvedAt.ShouldNotBeNull();
        verifiedAt.ShouldBe(resolvedAt);
        Guid.TryParseExact(
                ReadExtraString(
                    execution,
                    DataPipelineExecutionCoordinator.ResolvedByPropertyName),
                "N",
                out var resolvedBy)
            .ShouldBeTrue();
        resolvedBy.ShouldNotBe(Guid.Empty);
        execution.EvidenceProtocol.ShouldBe((short)1);
        execution.TargetResourceHash.ShouldNotBeNullOrWhiteSpace();
        execution.VerifiedTargetResourceHash.ShouldNotBeNullOrWhiteSpace();
        execution.ExecutionContractHash.ShouldNotBeNullOrWhiteSpace();
        execution.TargetResourceInstanceId.ShouldNotBeNull();

        var contextFactory = typeof(DataPipelineExecutionCoordinator)
            .GetMethod(
                "TryCreateReceiptContext",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "TryCreateReceiptContext test seam not found");
        var contextArguments = new object?[] { execution, null };
        ((bool)contextFactory.Invoke(null, contextArguments)!).ShouldBeTrue(
            "持久执行身份必须能重建 receipt-v1 上下文");
        var context = (EtlExecutionContext)contextArguments[1]!;

        var evidenceFactory = typeof(DataPipelineExecutionCoordinator)
            .GetMethod(
                "TryReadStoredReceiptEvidence",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "TryReadStoredReceiptEvidence test seam not found");
        var evidenceArguments = new object?[] { execution, context, null };
        ((bool)evidenceFactory.Invoke(null, evidenceArguments)!).ShouldBeTrue(
            "核验终态必须能从平台行重建完整回执证据");
        var evidence = (EtlTargetReceiptEvidence)evidenceArguments[2]!;

        var receiptMatcher = typeof(DataPipelineExecutionCoordinator)
            .GetMethod(
                "ReceiptMatchesExecution",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "ReceiptMatchesExecution test seam not found");
        ((bool)receiptMatcher.Invoke(
            null,
            new object[] { execution, context, evidence })!)
            .ShouldBeTrue(
                "持久回执证据必须与执行身份逐字段相等");

        var validator = typeof(DataPipelineExecutionCoordinator).GetMethod(
            "IsCompleteReceiptVerification",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "IsCompleteReceiptVerification test seam not found");
        ((bool)validator.Invoke(null, new object[] { execution })!)
            .ShouldBeTrue(
                "首笔机器核验写出的 marker 必须立即满足自己的幂等读取契约");
    }

    private sealed record ReceiptSeed(
        PipelineExecution Execution,
        string LegacyHash,
        string VerifiedHash,
        Guid TargetId);
}
