using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using SwapMid.Notification;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;

namespace SwapMid.DataIntegration;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class DataPipelineAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IDataPipelineAppService _dataPipelineAppService;
    private readonly IDataSourceAppService _dataSourceAppService;
    private readonly IPipelineExecutionAppService _pipelineExecutionAppService;
    private readonly IRepository<PipelineExecution, Guid> _executionRepository;
    private readonly FakeEtlPipelineRunner _etlRunner;
    private readonly TestDataPipelineExecutionFence _executionFence;
    private readonly TestPipelineTerminalStoreTopologyGuard _topologyGuard;
    private readonly FakeNotificationService _notificationService;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly IBackgroundJobStore _backgroundJobStore;
    private readonly IBackgroundJobSerializer _backgroundJobSerializer;
    private readonly AbpBackgroundJobWorkerOptions _backgroundJobWorkerOptions;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly DataPipelineExecutionCoordinator _executionCoordinator;
    private readonly FakeEtlTargetReceiptStore _receiptStore;

    public DataPipelineAppServiceTests()
    {
        _dataPipelineAppService = GetRequiredService<IDataPipelineAppService>();
        _dataSourceAppService = GetRequiredService<IDataSourceAppService>();
        _pipelineExecutionAppService = GetRequiredService<IPipelineExecutionAppService>();
        _executionRepository = GetRequiredService<IRepository<PipelineExecution, Guid>>();
        _etlRunner = (FakeEtlPipelineRunner)GetRequiredService<IEtlPipelineRunner>();
        _executionFence = GetRequiredService<TestDataPipelineExecutionFence>();
        _topologyGuard = GetRequiredService<
            TestPipelineTerminalStoreTopologyGuard>();
        _notificationService = (FakeNotificationService)GetRequiredService<INotificationService>();
        _currentUser = GetRequiredService<ICurrentUser>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _backgroundJobStore = GetRequiredService<IBackgroundJobStore>();
        _backgroundJobSerializer = GetRequiredService<IBackgroundJobSerializer>();
        _backgroundJobWorkerOptions = GetRequiredService<
            IOptions<AbpBackgroundJobWorkerOptions>>().Value;
        _distributedLock = GetRequiredService<IAbpDistributedLock>();
        _executionCoordinator = GetRequiredService<
            DataPipelineExecutionCoordinator>();
        _receiptStore = GetRequiredService<FakeEtlTargetReceiptStore>();
        _etlRunner.Reset();
        _executionFence.Reset();
        _topologyGuard.Reset();
        _notificationService.Reset();
        _receiptStore.Reset();
    }

    [Fact]
    public async Task Should_Create_And_Filter_Data_Pipeline_With_Source_Name()
    {
        var source = await CreateSourceAsync("Pipeline Source");
        var name = $"Pipeline {Guid.NewGuid():N}";

        var created = await _dataPipelineAppService.CreateAsync(new DataPipelineCreateUpdateDto
        {
            Name = name,
            Description = "Nightly customer sync",
            SourceId = source.Id,
            TransformConfig = "{\"steps\":[]}",
            ScheduleCron = "0 2 * * *",
            ScheduleEnabled = true
        });

        created.Id.ShouldNotBe(Guid.Empty);
        created.Name.ShouldBe(name);
        created.SourceId.ShouldBe(source.Id);
        created.ScheduleEnabled.ShouldBeTrue();
        created.Status.ShouldBe(PipelineStatus.Draft);

        var list = await _dataPipelineAppService.GetListAsync(new GetDataPipelineListInput
        {
            Filter = name,
            SourceId = source.Id,
            MaxResultCount = 10
        });

        list.TotalCount.ShouldBe(1);
        list.Items.Single().SourceName.ShouldBe(source.Name);
    }

    [Fact]
    public async Task Should_Activate_Execute_And_Pause_Data_Pipeline()
    {
        var source = await CreateSourceAsync("Executable Source");
        var target = await CreateSourceAsync("Executable Target");
        var created = await _dataPipelineAppService.CreateAsync(new DataPipelineCreateUpdateDto
        {
            Name = $"Executable Pipeline {Guid.NewGuid():N}",
            SourceId = source.Id,
            TargetId = target.Id,
            TransformConfig = CreateExecutableTransformConfig(
                "SELECT 1 AS Id"),
        });

        await _dataPipelineAppService.ActivateAsync(created.Id);

        var active = await _dataPipelineAppService.GetAsync(created.Id);
        active.Status.ShouldBe(PipelineStatus.Active);

        var execution = await _dataPipelineAppService.ExecuteAsync(
            created.Id,
            Guid.NewGuid());
        execution.PipelineId.ShouldBe(created.Id);
        // ExecuteAsync 会把执行期异常收敛为 Failed + 安全错误码；断言文本仅用于测试失败时
        // 输出脱敏说明，不得恢复底层异常正文。
        execution.Status.ShouldBe(ExecutionStatus.Succeeded,
            $"ETL 执行应成功,实际={execution.Status},ErrorMessage={execution.ErrorMessage}");

        var afterExecution = await _dataPipelineAppService.GetAsync(created.Id);
        afterExecution.LastRunStatus.ShouldBe(ExecutionStatus.Succeeded,
            $"管道最近执行状态应为成功,实际={afterExecution.LastRunStatus},执行单 ErrorMessage={execution.ErrorMessage}");
        afterExecution.LastRunAt.ShouldNotBeNull();

        await _dataPipelineAppService.PauseAsync(created.Id);

        var paused = await _dataPipelineAppService.GetAsync(created.Id);
        paused.Status.ShouldBe(PipelineStatus.Paused);
    }

    [Fact]
    public async Task Terminal_Store_Topology_Mismatch_Should_Reject_Before_Fence_Claim_And_Runner()
    {
        var pipeline = await CreateActivePipelineAsync(
            "Terminal Store Topology Mismatch");
        var runId = Guid.NewGuid();
        _topologyGuard.Reject();

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.ExecuteAsync(pipeline.Id, runId));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        _topologyGuard.Calls.ShouldContain(
            PipelineTerminalStoreTopologyPhase.ExecutionAdmission);
        _executionFence.AcquisitionCalls.ShouldBe(0);
        _etlRunner.RunCalls.ShouldBe(0);
        (await WithUnitOfWorkAsync(
                () => _executionRepository.FindAsync(runId)))
            .ShouldBeNull("拓扑拒绝必须发生在 Running 声明之前");
    }

    [Fact]
    public async Task Tenant_Caller_Execute_Should_Be_Rejected_Before_Fence_And_Restore_Tenant()
    {
        var pipeline = await CreateActivePipelineAsync(
            "Tenant Caller Host Fence");
        var tenantId = Guid.NewGuid();
        var originalTenantId = _currentTenant.Id;
        var runId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId, "etl-caller-tenant"))
        {
            _currentTenant.Id.ShouldBe(tenantId);
            await Should.ThrowAsync<AbpAuthorizationException>(() =>
                _dataPipelineAppService.ExecuteAsync(pipeline.Id, runId));
            _currentTenant.Id.ShouldBe(tenantId,
                "host-only 入口拒绝后必须保留调用方 tenant scope");
        }

        _executionFence.AcquisitionCalls.ShouldBe(0,
            "tenant 调用必须在获取执行 fence 前拒绝");
        _etlRunner.RunCalls.ShouldBe(0);
        (await WithUnitOfWorkAsync(
                () => _executionRepository.FindAsync(runId)))
            .ShouldBeNull("tenant 调用不得留下 Running 执行记录");
        _currentTenant.Id.ShouldBe(originalTenantId);
    }

    [Fact]
    public async Task Tenant_Caller_ConfirmedApplied_Should_Use_Host_Fence_And_Restore_Tenant()
    {
        var unknown = await CreateUnknownExecutionAsync(
            "Tenant Resolve Applied Host Fence");
        var tenantId = Guid.NewGuid();
        var originalTenantId = _currentTenant.Id;
        PipelineExecution resolved;

        using (_currentTenant.Change(tenantId, "etl-resolver-tenant"))
        {
            _currentTenant.Id.ShouldBe(tenantId);
            resolved = await _executionCoordinator.ResolveOutcomeAsync(
                unknown.Id,
                new ResolvePipelineExecutionOutcomeInput
                {
                    Resolution =
                        PipelineExecutionResolution.ConfirmedApplied,
                    ProcessedRows = 7,
                    VerificationNote = "incident:RESOLVE-HOST-SCOPE-0001",
                },
                _currentUser.Id ?? throw new InvalidOperationException(
                    "测试登录身份缺少 UserId"));
            _currentTenant.Id.ShouldBe(tenantId,
                "对账协调器退出时必须恢复调用方 tenant scope");
        }

        resolved.Status.ShouldBe(ExecutionStatus.Succeeded);
        _executionFence.LastObservedTenantId.ShouldBeNull(
            "ConfirmedApplied 虽不入通知队列，仍必须在 host 库取 fence");
        _topologyGuard.Calls.ShouldContain(
            PipelineTerminalStoreTopologyPhase.OutcomeResolutionApplied);
        _currentTenant.Id.ShouldBe(originalTenantId);
    }

    [Theory]
    [InlineData(PipelineTerminalStoreTopologyPhase.TerminalFinalize)]
    [InlineData(PipelineTerminalStoreTopologyPhase.NotificationSchedule)]
    public async Task Terminal_Store_Topology_Drift_Should_Keep_Running_And_Not_Queue_Notification(
        PipelineTerminalStoreTopologyPhase rejectedPhase)
    {
        var pipeline = await CreateActivePipelineAsync(
            $"Terminal Store Drift {rejectedPhase}");
        var runId = Guid.NewGuid();
        _etlRunner.ExceptionToThrow = new InvalidOperationException(
            "safe injected runner failure");
        _topologyGuard.RejectAt(rejectedPhase);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.ExecuteAsync(pipeline.Id, runId));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        _etlRunner.RunCalls.ShouldBe(1);
        _topologyGuard.Calls.ShouldContain(
            PipelineTerminalStoreTopologyPhase.ExecutionAdmission);
        _topologyGuard.Calls.ShouldContain(rejectedPhase);
        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(runId));
        stored.Status.ShouldBe(ExecutionStatus.Running);
        stored.FinishedAt.ShouldBeNull();
        stored.ExtraProperties.ContainsKey(
                PipelineExecutionFailurePolicy.ErrorCodePropertyName)
            .ShouldBeFalse();

        var waitingJobs = await WithUnitOfWorkAsync(() =>
            _backgroundJobStore.GetWaitingJobsAsync(
                _backgroundJobWorkerOptions.ApplicationName,
                1024));
        waitingJobs
            .Where(job => job.JobName ==
                BackgroundJobNameAttribute.GetName<
                    PipelineFailureNotificationJobArgs>())
            .Select(job => (PipelineFailureNotificationJobArgs)
                _backgroundJobSerializer.Deserialize(
                    job.JobArgs,
                    typeof(PipelineFailureNotificationJobArgs)))
            .ShouldNotContain(args => args.ExecutionId == runId);
    }

    [Fact]
    public async Task Should_Reject_Duplicate_Data_Pipeline_Name()
    {
        var source = await CreateSourceAsync("Duplicate Pipeline Source");
        var name = $"Duplicate Pipeline {Guid.NewGuid():N}";
        await _dataPipelineAppService.CreateAsync(new DataPipelineCreateUpdateDto
        {
            Name = name,
            SourceId = source.Id
        });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.CreateAsync(new DataPipelineCreateUpdateDto
            {
                Name = name,
                SourceId = source.Id
            }));

        exception.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.PipelineNameAlreadyExists);
    }

    [Fact]
    public async Task Execution_Failure_Should_Not_Expose_Exception_Details_Anywhere()
    {
        const string secret = "P1_SECRET_SENTINEL";
        var pipeline = await CreateActivePipelineAsync("Safe Failure");
        _etlRunner.ExceptionToThrow =
            new InvalidOperationException($"provider failed;Password={secret}");

        var execution = await _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            Guid.NewGuid());

        AssertSafeFailure(execution, typeof(InvalidOperationException), secret);

        var detail = await _pipelineExecutionAppService.GetAsync(execution.Id);
        AssertSafeFailure(detail, typeof(InvalidOperationException), secret);

        var list = await _pipelineExecutionAppService.GetListAsync(
            new GetPipelineExecutionListInput
            {
                PipelineId = pipeline.Id,
                MaxResultCount = 10
            });
        var listed = list.Items.Single(item => item.Id == execution.Id);
        AssertSafeFailure(listed, typeof(InvalidOperationException), secret);

        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(execution.Id));
        (stored.ErrorMessage ?? string.Empty).ShouldNotContain(secret);
        JsonSerializer.Serialize(stored.ExtraProperties).ShouldNotContain(secret);
        stored.ExtraProperties[PipelineExecutionFailurePolicy.ErrorCodePropertyName]
            .ShouldBe(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed);
        stored.ExtraProperties[PipelineExecutionFailurePolicy.CorrelationIdPropertyName]
            .ShouldBe(execution.CorrelationId);
        stored.ExtraProperties[PipelineExecutionFailurePolicy.ExceptionTypePropertyName]
            .ShouldBe(typeof(InvalidOperationException).FullName);

        _notificationService.Sent.ShouldBeEmpty(
            "终态路径只能耐久入队，通知网络必须由独立 background job 执行");
    }

    [Fact]
    public async Task Execute_And_Activation_Should_Use_Stable_Precondition_Error_Codes()
    {
        var source = await CreateSourceAsync("Precondition Source");
        var target = await CreateSourceAsync("Precondition Target");
        var inactive = await _dataPipelineAppService.CreateAsync(new DataPipelineCreateUpdateDto
        {
            Name = $"Inactive Pipeline {Guid.NewGuid():N}",
            SourceId = source.Id,
            TargetId = target.Id
        });

        var inactiveException = await Should.ThrowAsync<BusinessException>(
            () => _dataPipelineAppService.ExecuteAsync(
                inactive.Id,
                Guid.NewGuid()));
        inactiveException.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineNotActive);

        var targetMissing = await _dataPipelineAppService.CreateAsync(new DataPipelineCreateUpdateDto
        {
            Name = $"Target Missing Pipeline {Guid.NewGuid():N}",
            SourceId = source.Id
        });
        var targetException = await Should.ThrowAsync<BusinessException>(
            () => _dataPipelineAppService.ActivateAsync(targetMissing.Id));
        targetException.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineTargetNotConfigured);
        (await _dataPipelineAppService.GetAsync(targetMissing.Id))
            .Status.ShouldBe(PipelineStatus.Draft,
                "缺少目标的管道必须在激活阶段失败关闭");
        _etlRunner.RunCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Execute_Should_Reject_Empty_RunId_Before_Admission()
    {
        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.ExecuteAsync(
                Guid.NewGuid(),
                Guid.Empty));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionInputInvalid);
        _executionFence.AcquisitionCalls.ShouldBe(0);
        _etlRunner.RunCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Execution_Input_Error_Should_Preserve_Only_Stable_Error_Code()
    {
        var pipeline = await CreateActivePipelineAsync("Invalid Input");
        _etlRunner.ExceptionToThrow = new BusinessException(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);

        var execution = await _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            Guid.NewGuid());

        execution.ErrorCode.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
        execution.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
        execution.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        execution.ExceptionType.ShouldBe(typeof(BusinessException).FullName);
    }

    [Fact]
    public async Task Historical_Unsafe_Error_Message_Should_Be_Sanitized_On_Read()
    {
        const string secret = "P1_SECRET_SENTINEL";
        var source = await CreateSourceAsync("Legacy Failure Source");
        var pipeline = await _dataPipelineAppService.CreateAsync(new DataPipelineCreateUpdateDto
        {
            Name = $"Legacy Failure Pipeline {Guid.NewGuid():N}",
            SourceId = source.Id
        });
        var legacy = new PipelineExecution(
            Guid.NewGuid(), pipeline.Id, pipeline.Name, TriggerType.Manual);
        legacy.Fail(DateTime.UtcNow);
        var legacyErrorProperty = typeof(PipelineExecution).GetProperty(
            nameof(PipelineExecution.ErrorMessage))
            ?? throw new InvalidOperationException("PipelineExecution.ErrorMessage property not found.");
        legacyErrorProperty.SetValue(legacy, $"Password={secret}");
        await WithUnitOfWorkAsync(
            () => _executionRepository.InsertAsync(legacy, autoSave: true));

        var detail = await _pipelineExecutionAppService.GetAsync(legacy.Id);
        AssertLegacyFailureIsSafe(detail, secret);

        var list = await _pipelineExecutionAppService.GetListAsync(
            new GetPipelineExecutionListInput
            {
                PipelineId = pipeline.Id,
                MaxResultCount = 10
            });
        AssertLegacyFailureIsSafe(
            list.Items.Single(item => item.Id == legacy.Id), secret);
    }

    [Fact]
    public async Task Notification_Delivery_Should_Not_Run_Inside_Terminal_Path()
    {
        const string runnerSecret = "P1_SECRET_SENTINEL";
        const string notificationSecret = "P1_NOTIFICATION_SECRET_SENTINEL"; // gitleaks:allow -- 固定非密钥测试哨兵
        var pipeline = await CreateActivePipelineAsync("Notification Failure");
        _etlRunner.ExceptionToThrow =
            new InvalidOperationException($"provider failed;Password={runnerSecret}");
        _notificationService.ExceptionToThrow =
            new InvalidOperationException($"webhook failed;token={notificationSecret}");

        var execution = await _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            Guid.NewGuid());

        AssertSafeFailure(
            execution,
            typeof(InvalidOperationException),
            runnerSecret,
            notificationSecret);
        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(execution.Id));
        stored.Status.ShouldBe(ExecutionStatus.Failed);
        _notificationService.Sent.ShouldBeEmpty(
            "即使通知服务会抛异常，终态路径也不得调用它");
        AssertDoesNotContainSecrets(
            new
            {
                stored.ErrorMessage,
                stored.ExtraProperties
            },
            runnerSecret,
            notificationSecret);
    }

    [Fact]
    public async Task Execute_Should_Reject_Ambient_UnitOfWork_Before_External_Etl()
    {
        var pipeline = await CreateActivePipelineAsync("Ambient Uow Rejected");

        await WithUnitOfWorkAsync(async () =>
        {
            var exception = await Should.ThrowAsync<InvalidOperationException>(
                () => _dataPipelineAppService.ExecuteAsync(
                    pipeline.Id,
                    Guid.NewGuid()));
            exception.Message.ShouldContain("ambient UoW");
        });

        _etlRunner.RunCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Failure_Notification_Should_Be_Queued_Without_Inline_Delivery()
    {
        var pipeline = await CreateActivePipelineAsync("Independent Failure Commit");
        _etlRunner.ExceptionToThrow = new InvalidOperationException("safe test failure");

        var execution = await _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            Guid.NewGuid());
        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(execution.Id));

        stored.Status.ShouldBe(ExecutionStatus.Failed);
        _notificationService.Sent.ShouldBeEmpty(
            "失败终态提交完成时不应等待通知网络");

        var waitingJobs = await WithUnitOfWorkAsync(() =>
            _backgroundJobStore.GetWaitingJobsAsync(
                _backgroundJobWorkerOptions.ApplicationName,
                1024));
        var queued = waitingJobs
            .Where(job => job.JobName ==
                BackgroundJobNameAttribute.GetName<
                    PipelineFailureNotificationJobArgs>())
            .Select(job => new
            {
                Job = job,
                Args = (PipelineFailureNotificationJobArgs)
                    _backgroundJobSerializer.Deserialize(
                        job.JobArgs,
                        typeof(PipelineFailureNotificationJobArgs)),
            })
            .Where(item => item.Args.ExecutionId == execution.Id)
            .ShouldHaveSingleItem();
        queued.Args.PipelineId.ShouldBe(pipeline.Id);
        queued.Args.ErrorCode.ShouldBe(execution.ErrorCode);
        queued.Args.CorrelationId.ShouldBe(execution.CorrelationId);
        queued.Args.ExceptionType.ShouldBe(execution.ExceptionType);
        stored.ExtraProperties[
                PipelineExecutionFailurePolicy.ErrorCodePropertyName]
            .ShouldBe(queued.Args.ErrorCode);
        stored.ExtraProperties[
                PipelineExecutionFailurePolicy.CorrelationIdPropertyName]
            .ShouldBe(queued.Args.CorrelationId);
        stored.ExtraProperties[
                PipelineExecutionFailurePolicy.ExceptionTypePropertyName]
            .ShouldBe(queued.Args.ExceptionType);
        queued.Job.IsAbandoned.ShouldBeFalse();
        await WithUnitOfWorkAsync(() =>
            _backgroundJobStore.DeleteAsync(queued.Job.Id));
    }

    [Fact]
    public async Task Running_Should_Be_Committed_Before_External_Etl_And_Runner_Should_Have_No_Ambient_Uow()
    {
        var pipeline = await CreateActivePipelineAsync("Running Visibility");
        var runId = Guid.NewGuid();
        _etlRunner.Block();

        var executionTask = _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            runId);
        await _etlRunner.RunEntered.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            var running = await WithUnitOfWorkAsync(
                () => _executionRepository.GetAsync(runId));
            running.Status.ShouldBe(ExecutionStatus.Running);
            running.FinishedAt.ShouldBeNull();
            _etlRunner.ObservedAmbientUnitOfWork.ShouldBe(false);
            _etlRunner.LastCancellationToken.CanBeCanceled.ShouldBeTrue();
        }
        finally
        {
            _etlRunner.Release();
        }

        var completed = await executionTask;
        completed.Status.ShouldBe(ExecutionStatus.Succeeded);
    }

    [Fact]
    public async Task Same_RunId_Should_Replay_Terminal_Result_Without_Rerunning_Etl()
    {
        var pipeline = await CreateActivePipelineAsync("Idempotent Run");
        var runId = Guid.NewGuid();
        _etlRunner.Result = new EtlRunResult(37, TargetTruncated: true);

        var first = await _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            runId);
        var replay = await _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            runId);

        first.Id.ShouldBe(runId);
        replay.Id.ShouldBe(first.Id);
        replay.Status.ShouldBe(ExecutionStatus.Succeeded);
        replay.ProcessedRows.ShouldBe(37);
        _etlRunner.RunCalls.ShouldBe(1);
        _executionFence.AcquisitionCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_Run_For_Same_Pipeline_Should_Return_Busy_Without_Starting_Second_Etl()
    {
        var pipeline = await CreateActivePipelineAsync("Pipeline Busy");
        _etlRunner.Block();
        var firstTask = _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            Guid.NewGuid());
        await _etlRunner.RunEntered.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            var exception = await Should.ThrowAsync<BusinessException>(() =>
                _dataPipelineAppService.ExecuteAsync(
                    pipeline.Id,
                    Guid.NewGuid()));
            exception.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionBusy);
            _etlRunner.RunCalls.ShouldBe(1);
        }
        finally
        {
            _etlRunner.Release();
        }

        (await firstTask).Status.ShouldBe(ExecutionStatus.Succeeded);
    }

    [Fact]
    public async Task Concurrent_Pipelines_For_Same_Target_Should_Fail_Closed_At_Database_Fence()
    {
        var firstSource = await CreateSourceAsync("Fence Source One");
        var secondSource = await CreateSourceAsync("Fence Source Two");
        var sharedTarget = await CreateSourceAsync("Fence Shared Target");
        var firstPipeline = await CreateActivePipelineAsync(
            "Fence Pipeline One",
            firstSource.Id,
            sharedTarget.Id,
            "SELECT 1 AS Id");
        var secondPipeline = await CreateActivePipelineAsync(
            "Fence Pipeline Two",
            secondSource.Id,
            sharedTarget.Id,
            "SELECT 2 AS Id");
        _etlRunner.Block();
        var firstTask = _dataPipelineAppService.ExecuteAsync(
            firstPipeline.Id,
            Guid.NewGuid());
        await _etlRunner.RunEntered.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            var exception = await Should.ThrowAsync<BusinessException>(() =>
                _dataPipelineAppService.ExecuteAsync(
                    secondPipeline.Id,
                    Guid.NewGuid()));
            exception.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionCoordinationUnavailable);
            _executionFence.AcquisitionCalls.ShouldBe(2);
            _etlRunner.RunCalls.ShouldBe(1);
        }
        finally
        {
            _etlRunner.Release();
        }

        (await firstTask).Status.ShouldBe(ExecutionStatus.Succeeded);
    }

    [Fact]
    public async Task Global_Capacity_Should_Reject_Third_Run_Release_One_Slot_And_Not_Block_Resolution()
    {
        var unknown = await CreateUnknownExecutionAsync(
            "Capacity Independent Resolution");
        unknown.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        _etlRunner.Reset();
        _executionFence.Reset();

        var thirdPipeline = await CreateActivePipelineAsync(
            "Capacity Pipeline Three");
        var firstReservation = await _executionFence.TryAcquireAsync(
            Guid.NewGuid(),
            new[] { $"capacity-test-target-a:{Guid.NewGuid():N}" },
            capacitySlots: 2);
        firstReservation.Rejection.ShouldBe(
            DataPipelineExecutionFenceRejection.None);
        firstReservation.Handle.ShouldNotBeNull();
        var firstHandle = firstReservation.Handle!;
        var secondReservation = await _executionFence.TryAcquireAsync(
            Guid.NewGuid(),
            new[] { $"capacity-test-target-b:{Guid.NewGuid():N}" },
            capacitySlots: 2);
        secondReservation.Rejection.ShouldBe(
            DataPipelineExecutionFenceRejection.None);
        secondReservation.Handle.ShouldNotBeNull();
        var secondHandle = secondReservation.Handle!;
        var firstReleased = false;

        try
        {
            // 人工对账只争用对应 pipeline/target，不占正常执行的全局容量槽。
            var resolved = await _pipelineExecutionAppService.ResolveOutcomeAsync(
                unknown.Id,
                new ResolvePipelineExecutionOutcomeInput
                {
                    Resolution = PipelineExecutionResolution.ConfirmedApplied,
                    ProcessedRows = 0,
                    VerificationNote = "incident:RESOLVE-CAPACITY-0001",
                });
            resolved.Status.ShouldBe(ExecutionStatus.Succeeded);
            _etlRunner.RunCalls.ShouldBe(0);

            var thirdRunId = Guid.NewGuid();
            var capacityException = await Should.ThrowAsync<BusinessException>(
                () => _dataPipelineAppService.ExecuteAsync(
                    thirdPipeline.Id,
                    thirdRunId));
            capacityException.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionCapacityExhausted);
            _etlRunner.RunCalls.ShouldBe(0);
            var rejectedExecution = await WithUnitOfWorkAsync(
                () => _executionRepository.FindAsync(thirdRunId));
            rejectedExecution.ShouldBeNull(
                "容量拒绝必须发生在 Running 声明与 runner 启动之前");

            await firstHandle.DisposeAsync();
            firstReleased = true;
            var retried = await _dataPipelineAppService.ExecuteAsync(
                thirdPipeline.Id,
                thirdRunId);
            retried.Id.ShouldBe(thirdRunId);
            retried.Status.ShouldBe(ExecutionStatus.Succeeded);
            _etlRunner.RunCalls.ShouldBe(1,
                "释放一个容量槽后同 RunId 必须能够进入 runner");
        }
        finally
        {
            if (!firstReleased)
            {
                await firstHandle.DisposeAsync();
            }

            await secondHandle.DisposeAsync();
        }
    }

    [Fact]
    public async Task Caller_Cancellation_Should_Reach_Runner_And_Persist_Cancelled()
    {
        var pipeline = await CreateActivePipelineAsync("Caller Cancellation");
        var runId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        _etlRunner.Block();
        var executionTask = _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            runId,
            cancellation.Token);
        await _etlRunner.RunEntered.WaitAsync(TimeSpan.FromSeconds(10));

        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await executionTask);

        _etlRunner.LastCancellationToken.IsCancellationRequested.ShouldBeTrue();
        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(runId));
        stored.Status.ShouldBe(ExecutionStatus.Cancelled);
        stored.FinishedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Database_Fence_Lease_Loss_Should_Stop_Runner_And_Persist_Coordination_Failure()
    {
        var pipeline = await CreateActivePipelineAsync("Fence Lease Loss");
        var runId = Guid.NewGuid();
        _etlRunner.Block();
        var executionTask = _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            runId);
        await _etlRunner.RunEntered.WaitAsync(TimeSpan.FromSeconds(10));

        _executionFence.LoseCurrentLease();
        var failed = await executionTask.WaitAsync(TimeSpan.FromSeconds(10));

        _etlRunner.LastCancellationToken.IsCancellationRequested.ShouldBeTrue();
        failed.Status.ShouldBe(ExecutionStatus.Failed);
        failed.ErrorCode.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(runId));
        stored.Status.ShouldBe(ExecutionStatus.Failed);
        ReadExtraString(
                stored,
                PipelineExecutionFailurePolicy.ErrorCodePropertyName)
            .ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionCoordinationUnavailable);
    }

    [Fact]
    public async Task Unknown_Target_Commit_Should_Be_Durable_Idempotent_And_Block_New_Run()
    {
        var pipeline = await CreateActivePipelineAsync("Unknown Commit");
        var runId = Guid.NewGuid();
        _etlRunner.ExceptionToThrow =
            new EtlTargetCommitOutcomeUnknownException();

        var unknown = await _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            runId);
        var replay = await _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            runId);

        unknown.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        unknown.ErrorCode.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionOutcomeUnknown);
        replay.Id.ShouldBe(runId);
        replay.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        _etlRunner.RunCalls.ShouldBe(1);

        var blocked = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.ExecuteAsync(
                pipeline.Id,
                Guid.NewGuid()));
        blocked.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionOutcomeUnresolved);
        _etlRunner.RunCalls.ShouldBe(1);

        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(runId));
        stored.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
    }

    [Fact]
    public async Task OutcomeUnknown_On_Same_Target_Should_Block_Different_Pipeline_Without_Starting_Runner()
    {
        var firstSource = await CreateSourceAsync("Quarantine Source One");
        var secondSource = await CreateSourceAsync("Quarantine Source Two");
        var sharedTarget = await CreateSourceAsync("Quarantine Shared Target");
        var firstPipeline = await CreateActivePipelineAsync(
            "Quarantine Pipeline One",
            firstSource.Id,
            sharedTarget.Id,
            "SELECT 1 AS Id");
        var secondPipeline = await CreateActivePipelineAsync(
            "Quarantine Pipeline Two",
            secondSource.Id,
            sharedTarget.Id,
            "SELECT 2 AS Id");
        firstPipeline.Id.ShouldNotBe(secondPipeline.Id);
        _etlRunner.ExceptionToThrow =
            new EtlTargetCommitOutcomeUnknownException();
        var firstRun = await _dataPipelineAppService.ExecuteAsync(
            firstPipeline.Id,
            Guid.NewGuid());
        firstRun.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        _etlRunner.ExceptionToThrow = null;
        var secondRunId = Guid.NewGuid();

        var blocked = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.ExecuteAsync(
                secondPipeline.Id,
                secondRunId));

        blocked.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionOutcomeUnresolved);
        _etlRunner.RunCalls.ShouldBe(1);
        var storedFirst = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(firstRun.Id));
        storedFirst.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        var secondExecution = await WithUnitOfWorkAsync(
            () => _executionRepository.FindAsync(secondRunId));
        secondExecution.ShouldBeNull();
    }

    [Fact]
    public async Task Soft_Deleted_Pipeline_Must_Not_Remove_Target_Quarantine()
    {
        var firstSource = await CreateSourceAsync("Deleted Quarantine Source One");
        var secondSource = await CreateSourceAsync("Deleted Quarantine Source Two");
        var sharedTarget = await CreateSourceAsync("Deleted Quarantine Shared Target");
        var firstPipeline = await CreateActivePipelineAsync(
            "Deleted Quarantine Pipeline One",
            firstSource.Id,
            sharedTarget.Id,
            "SELECT 1 AS Id");
        var secondPipeline = await CreateActivePipelineAsync(
            "Deleted Quarantine Pipeline Two",
            secondSource.Id,
            sharedTarget.Id,
            "SELECT 2 AS Id");
        _etlRunner.ExceptionToThrow =
            new EtlTargetCommitOutcomeUnknownException();
        var firstRun = await _dataPipelineAppService.ExecuteAsync(
            firstPipeline.Id,
            Guid.NewGuid());
        firstRun.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        _etlRunner.ExceptionToThrow = null;

        await _dataPipelineAppService.DeleteAsync(firstPipeline.Id);

        var storedFirst = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(firstRun.Id));
        storedFirst.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        var secondRunId = Guid.NewGuid();
        var blocked = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.ExecuteAsync(
                secondPipeline.Id,
                secondRunId));
        blocked.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionOutcomeUnresolved);
        _etlRunner.RunCalls.ShouldBe(1,
            "软删除流水线不得删除未知态证据或解除共享目标隔离");
        var secondExecution = await WithUnitOfWorkAsync(
            () => _executionRepository.FindAsync(secondRunId));
        secondExecution.ShouldBeNull();
    }

    [Fact]
    public async Task OutcomeUnknown_Should_Quarantine_Same_Resource_Group_Across_Host_Aliases()
    {
        var firstSource = await CreateSourceAsync("Alias Source One");
        var secondSource = await CreateSourceAsync("Alias Source Two");
        var resourceGroup = $"test:shared-alias-{Guid.NewGuid():N}";
        var hostnameTarget = await CreateSourceAsync(
            "Alias Target Hostname",
            targetResourceGroup: resourceGroup,
            host: "localhost");
        var ipTarget = await CreateSourceAsync(
            "Alias Target Ip",
            targetResourceGroup: resourceGroup,
            host: "127.0.0.1");
        var firstPipeline = await CreateActivePipelineAsync(
            "Alias Pipeline One",
            firstSource.Id,
            hostnameTarget.Id,
            "SELECT 1 AS Id");
        var secondPipeline = await CreateActivePipelineAsync(
            "Alias Pipeline Two",
            secondSource.Id,
            ipTarget.Id,
            "SELECT 2 AS Id");
        _etlRunner.ExceptionToThrow =
            new EtlTargetCommitOutcomeUnknownException();

        var firstRun = await _dataPipelineAppService.ExecuteAsync(
            firstPipeline.Id,
            Guid.NewGuid());
        firstRun.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        firstRun.TargetResourceGroup.ShouldBe(resourceGroup);
        firstRun.TargetDataSourceId.ShouldBe(hostnameTarget.Id);
        firstRun.TargetDataSourceName.ShouldBe(hostnameTarget.Name);
        firstRun.TargetProvider.ShouldBe("sqlserver");
        firstRun.TargetEndpoint.ShouldBe("localhost:1433");
        firstRun.TargetDatabase.ShouldBe("SwapMid");
        firstRun.TargetTable.ShouldBe("dbo.Target");
        firstRun.TargetConfigVersion.ShouldNotBeNullOrWhiteSpace();
        firstRun.SourceDataSourceId.ShouldBe(firstSource.Id);
        firstRun.SourceDataSourceName.ShouldBe(firstSource.Name);
        firstRun.SourceConfigVersion.ShouldNotBeNullOrWhiteSpace();
        firstRun.WriteMode.ShouldBe("append");
        firstRun.SourceQuerySha256.ShouldBe(Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes("SELECT 1 AS Id"))));
        _etlRunner.ExceptionToThrow = null;
        var secondRunId = Guid.NewGuid();

        var blocked = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.ExecuteAsync(
                secondPipeline.Id,
                secondRunId));

        blocked.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionOutcomeUnresolved);
        _etlRunner.RunCalls.ShouldBe(1,
            "host 别名不得绕过同一 targetResourceGroup 的未知态隔离");
        var secondExecution = await WithUnitOfWorkAsync(
            () => _executionRepository.FindAsync(secondRunId));
        secondExecution.ShouldBeNull();
    }

    [Fact]
    public async Task Missing_Target_Resource_Group_Should_Fail_Before_Claim_And_Runner()
    {
        var source = await CreateSourceAsync("Missing Group Source");
        var target = await CreateSourceAsync(
            "Missing Group Target",
            includeTargetResourceGroup: false);
        var pipeline = await _dataPipelineAppService.CreateAsync(
            new DataPipelineCreateUpdateDto
            {
                Name = $"Missing Group Pipeline {Guid.NewGuid():N}",
                SourceId = source.Id,
                TargetId = target.Id,
                TransformConfig = CreateExecutableTransformConfig(
                    "SELECT 1 AS Id"),
            });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.ActivateAsync(pipeline.Id));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineTargetReceiptInvalid);
        _executionFence.AcquisitionCalls.ShouldBe(0,
            "缺少稳定资源组必须在激活阶段、获取执行 fence 前失败");
        _etlRunner.RunCalls.ShouldBe(0);
        (await _dataPipelineAppService.GetAsync(pipeline.Id))
            .Status.ShouldBe(PipelineStatus.Draft,
                "ETL:025 不得留下可执行的 Active 管道");
    }

    [Fact]
    public async Task Running_On_Same_Target_Should_Remain_Running_And_Block_Different_Pipeline()
    {
        var firstSource = await CreateSourceAsync("Interrupted Source One");
        var secondSource = await CreateSourceAsync("Interrupted Source Two");
        var sharedTarget = await CreateSourceAsync("Interrupted Shared Target");
        var firstPipeline = await CreateActivePipelineAsync(
            "Interrupted Pipeline One",
            firstSource.Id,
            sharedTarget.Id,
            "SELECT 1 AS Id");
        var secondPipeline = await CreateActivePipelineAsync(
            "Interrupted Pipeline Two",
            secondSource.Id,
            sharedTarget.Id,
            "SELECT 2 AS Id");
        var completed = await _dataPipelineAppService.ExecuteAsync(
            firstPipeline.Id,
            Guid.NewGuid());
        var completedStored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(completed.Id));
        var targetResource = ReadExtraString(
            completedStored,
            DataPipelineExecutionCoordinator.TargetResourcePropertyName);
        targetResource.ShouldNotBeNullOrWhiteSpace();

        var interruptedId = Guid.NewGuid();
        var interrupted = new PipelineExecution(
            interruptedId,
            firstPipeline.Id,
            firstPipeline.Name,
            TriggerType.Manual,
            completedStored.TargetResourceHash,
            completedStored.VerifiedTargetResourceHash,
            completedStored.EvidenceProtocol,
            completedStored.TargetResourceInstanceId,
            completedStored.ExecutionContractHash,
            completedStored.TargetCoordinationIdentityHash);
        interrupted.SetProperty(
            DataPipelineExecutionCoordinator.TargetResourcePropertyName,
            targetResource);
        await WithUnitOfWorkAsync(() =>
            _executionRepository.InsertAsync(
                interrupted,
                autoSave: true));
        _etlRunner.Reset();
        _executionFence.Reset();
        var secondRunId = Guid.NewGuid();

        var blocked = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.ExecuteAsync(
                secondPipeline.Id,
                secondRunId));

        blocked.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionTargetBusy);
        _etlRunner.RunCalls.ShouldBe(0);
        var replay = await _dataPipelineAppService.ExecuteAsync(
            firstPipeline.Id,
            interruptedId);
        replay.Id.ShouldBe(interruptedId);
        replay.Status.ShouldBe(ExecutionStatus.Running);
        _etlRunner.RunCalls.ShouldBe(0);
        _executionFence.AcquisitionCalls.ShouldBe(2);
        var storedInterrupted = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(interruptedId));
        storedInterrupted.Status.ShouldBe(ExecutionStatus.Running);
        storedInterrupted.FinishedAt.ShouldBeNull();
        storedInterrupted.ExtraProperties.ContainsKey(
                PipelineExecutionFailurePolicy.ErrorCodePropertyName)
            .ShouldBeFalse();
        _notificationService.Sent.ShouldBeEmpty();
        var secondExecution = await WithUnitOfWorkAsync(
            () => _executionRepository.FindAsync(secondRunId));
        secondExecution.ShouldBeNull();
    }

    [Fact]
    public async Task RecoverRunning_Should_Reject_Invalid_Attestations_Without_Taking_Ownership()
    {
        var (_, running) = await CreateRecoverableRunningExecutionAsync(
            "Recovery Invalid Attestation");
        var now = DateTimeOffset.UtcNow;
        var invalidInputs = new[]
        {
            ChangeRecoveryInput(CreateRecoveryInput(running, now),
                input => input.RecoveryRequestId = Guid.Empty),
            ChangeRecoveryInput(CreateRecoveryInput(running, now),
                input => input.OldOwnerExitVerifiedAt = default),
            ChangeRecoveryInput(CreateRecoveryInput(running, now), input =>
            {
                input.DrainStartedAt = now.AddMinutes(-5);
                input.OldOwnerExitVerifiedAt = now.AddMinutes(-4);
                input.TargetSessionExitVerifiedAt = now.AddMinutes(-3);
            }),
            ChangeRecoveryInput(CreateRecoveryInput(running, now),
                input => input.DrainStartedAt = now.AddMinutes(-11)),
            ChangeRecoveryInput(CreateRecoveryInput(running, now),
                input => input.OldOwnerExitVerifiedAt = now.AddMinutes(-9)),
            ChangeRecoveryInput(CreateRecoveryInput(running, now),
                input => input.TargetSessionExitVerifiedAt =
                    now.AddMinutes(1)),
            ChangeRecoveryInput(CreateRecoveryInput(running, now),
                input => input.EvidenceReference =
                    "INC-2026\u200EUNSAFE-EVIDENCE"),
        };

        foreach (var input in invalidInputs)
        {
            var exception = await Should.ThrowAsync<BusinessException>(() =>
                _pipelineExecutionAppService.RecoverRunningAsync(
                    running.Id,
                    input));
            exception.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionRecoveryInvalid);
        }

        var emptyExecutionId = await Should.ThrowAsync<BusinessException>(() =>
            _pipelineExecutionAppService.RecoverRunningAsync(
                Guid.Empty,
                CreateRecoveryInput(running, now)));
        emptyExecutionId.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionRecoveryInvalid);

        _executionFence.AcquisitionCalls.ShouldBe(0,
            "无效证明必须在获取 PostgreSQL fence 前拒绝");
        _etlRunner.RunCalls.ShouldBe(0);
        (await LoadExecutionAsync(running.Id)).Status.ShouldBe(
            ExecutionStatus.Running);
        (await LoadRecoveryJobsAsync(running.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task RecoverRunning_Should_Reject_Stale_Stamp()
    {
        var (_, running) = await CreateRecoverableRunningExecutionAsync(
            "Recovery Stale Stamp");
        var stale = ChangeRecoveryInput(
            CreateRecoveryInput(running),
            input => input.ExpectedConcurrencyStamp =
                Guid.NewGuid().ToString("N"));

        var conflict = await Should.ThrowAsync<BusinessException>(() =>
            _pipelineExecutionAppService.RecoverRunningAsync(
                running.Id,
                stale));
        conflict.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionRecoveryConflict);
        (await LoadExecutionAsync(running.Id)).Status.ShouldBe(
            ExecutionStatus.Running);
        _etlRunner.RunCalls.ShouldBe(0);
    }

    [Fact]
    public async Task RecoverRunning_Should_Fail_Closed_On_Partial_Same_Request_Marker()
    {
        var (_, running) = await CreateRecoverableRunningExecutionAsync(
            "Recovery Partial Marker");
        var requestId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var tracked = await _executionRepository.GetAsync(running.Id);
            tracked.SetProperty(
                DataPipelineExecutionCoordinator
                    .RecoveryRequestIdPropertyName,
                requestId.ToString("N"));
            await _executionRepository.UpdateAsync(
                tracked,
                autoSave: true);
        });
        var marked = await LoadExecutionAsync(running.Id);
        var input = ChangeRecoveryInput(
            CreateRecoveryInput(marked),
            value => value.RecoveryRequestId = requestId);
        _executionFence.Reset();

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _pipelineExecutionAppService.RecoverRunningAsync(
                marked.Id,
                input));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        _executionFence.AcquisitionCalls.ShouldBe(0,
            "残缺 marker 不得被误当成成功回放后继续争锁或修补");
        _etlRunner.RunCalls.ShouldBe(0);
        (await LoadExecutionAsync(marked.Id)).Status.ShouldBe(
            ExecutionStatus.Running);
        (await LoadRecoveryJobsAsync(marked.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Same_Recovery_Request_Must_Match_All_Attested_Fields_Before_And_After_Resolution()
    {
        var (_, running) = await CreateRecoverableRunningExecutionAsync(
            "Recovery Replay Attestation Match");
        var recovererId = _currentUser.Id ?? throw new InvalidOperationException(
            "测试登录身份缺少 UserId");
        var input = CreateRecoveryInput(running);
        var recovered = await _executionCoordinator.RecoverRunningAsync(
            running.Id,
            input,
            recovererId);
        recovered.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);

        var mismatches = new[]
        {
            new RecoveryReplayAttempt(
                CloneRecoveryInput(input),
                Guid.NewGuid()),
            new RecoveryReplayAttempt(
                ChangeRecoveryInput(CloneRecoveryInput(input), value =>
                    value.ExpectedConcurrencyStamp =
                        Guid.NewGuid().ToString("N")),
                recovererId),
            new RecoveryReplayAttempt(
                ChangeRecoveryInput(CloneRecoveryInput(input), value =>
                    value.DrainStartedAt =
                        value.DrainStartedAt.AddSeconds(1)),
                recovererId),
            new RecoveryReplayAttempt(
                ChangeRecoveryInput(CloneRecoveryInput(input), value =>
                    value.OldOwnerExitVerifiedAt =
                        value.OldOwnerExitVerifiedAt.AddSeconds(1)),
                recovererId),
            new RecoveryReplayAttempt(
                ChangeRecoveryInput(CloneRecoveryInput(input), value =>
                    value.TargetSessionExitVerifiedAt =
                        value.TargetSessionExitVerifiedAt.AddSeconds(1)),
                recovererId),
            new RecoveryReplayAttempt(
                ChangeRecoveryInput(CloneRecoveryInput(input), value =>
                    value.EvidenceReference =
                        "incident:INC-2026-ALTERED-EVIDENCE"),
                recovererId),
            new RecoveryReplayAttempt(
                ChangeRecoveryInput(CloneRecoveryInput(input), value =>
                    value.EvidenceReference =
                        "https://evidence.invalid/case?token=secret"),
                recovererId),
        };

        await AssertRecoveryReplayMismatchesAsync(
            running.Id,
            input,
            mismatches,
            ExecutionStatus.OutcomeUnknown);

        var resolved = await _pipelineExecutionAppService.ResolveOutcomeAsync(
            running.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution.ConfirmedApplied,
                ProcessedRows = 23,
                VerificationNote = "incident:RECOVERY-RESOLUTION-0001",
            });
        resolved.Status.ShouldBe(ExecutionStatus.Succeeded);

        await AssertRecoveryReplayMismatchesAsync(
            running.Id,
            input,
            mismatches,
            ExecutionStatus.Succeeded);
    }

    [Fact]
    public async Task Corrupted_Recovery_Marker_Must_Fail_Closed_Before_Taking_Fence()
    {
        var corruptions = new[]
        {
            (
                DataPipelineExecutionCoordinator.RecoveredByPropertyName,
                Guid.Empty.ToString("N")),
            (
                DataPipelineExecutionCoordinator
                    .RecoveryEvidenceReferencePropertyName,
                "dba-evidence:x1"),
            (
                DataPipelineExecutionCoordinator
                    .RecoveryDrainStartedAtPropertyName,
                "not-a-timestamp"),
            (
                DataPipelineExecutionCoordinator
                    .RecoveryProtocolVersionPropertyName,
                "2"),
        };

        foreach (var (propertyName, corruptedValue) in corruptions)
        {
            var (_, running) = await CreateRecoverableRunningExecutionAsync(
                $"Recovery Corrupted Marker {propertyName}");
            var input = CreateRecoveryInput(running);
            await _pipelineExecutionAppService.RecoverRunningAsync(
                running.Id,
                input);
            await WithUnitOfWorkAsync(async () =>
            {
                var tracked = await _executionRepository.GetAsync(running.Id);
                tracked.SetProperty(propertyName, corruptedValue);
                await _executionRepository.UpdateAsync(
                    tracked,
                    autoSave: true);
            });
            var corrupted = await LoadExecutionAsync(running.Id);
            var corruptedStamp = corrupted.ConcurrencyStamp;
            var fenceCalls = _executionFence.AcquisitionCalls;
            var replayInput = propertyName ==
                              DataPipelineExecutionCoordinator
                                  .RecoveredByPropertyName
                ? ChangeRecoveryInput(
                    CloneRecoveryInput(input),
                    value => value.EvidenceReference = "dba-evidence:x1")
                : input;

            var exception = await Should.ThrowAsync<BusinessException>(() =>
                _pipelineExecutionAppService.RecoverRunningAsync(
                    running.Id,
                    replayInput));

            exception.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionCoordinationUnavailable);
            _executionFence.AcquisitionCalls.ShouldBe(
                fenceCalls,
                "损坏的恢复 marker 必须在争锁前失败关闭");
            var unchanged = await LoadExecutionAsync(running.Id);
            unchanged.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
            unchanged.ConcurrencyStamp.ShouldBe(corruptedStamp);
            (await LoadRecoveryJobsAsync(running.Id)).Count.ShouldBe(1);
            _etlRunner.RunCalls.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Recovery_Audit_Residue_Without_RequestId_Must_Block_All_Recoveries_And_Late_Finalizers()
    {
        var (_, running) = await CreateRecoverableRunningExecutionAsync(
            "Recovery Audit Residue");
        var input = CreateRecoveryInput(running);
        await _pipelineExecutionAppService.RecoverRunningAsync(
            running.Id,
            input);
        await WithUnitOfWorkAsync(async () =>
        {
            var tracked = await _executionRepository.GetAsync(running.Id);
            tracked.ExtraProperties.Remove(
                    DataPipelineExecutionCoordinator
                        .RecoveryRequestIdPropertyName)
                .ShouldBeTrue();
            await _executionRepository.UpdateAsync(
                tracked,
                autoSave: true);
        });

        var residue = await LoadExecutionAsync(running.Id);
        foreach (var retainedKey in new[]
                 {
                     DataPipelineExecutionCoordinator.RecoveredAtPropertyName,
                     DataPipelineExecutionCoordinator.RecoveredByPropertyName,
                     DataPipelineExecutionCoordinator
                         .RecoveryExpectedConcurrencyStampPropertyName,
                     DataPipelineExecutionCoordinator
                         .RecoveryDrainStartedAtPropertyName,
                     DataPipelineExecutionCoordinator
                         .RecoveryOldOwnerExitVerifiedAtPropertyName,
                     DataPipelineExecutionCoordinator
                         .RecoveryTargetSessionExitVerifiedAtPropertyName,
                     DataPipelineExecutionCoordinator
                         .RecoveryEvidenceReferencePropertyName,
                     DataPipelineExecutionCoordinator
                         .RecoveryProtocolVersionPropertyName,
                 })
        {
            residue.ExtraProperties.ContainsKey(retainedKey).ShouldBeTrue();
        }

        var fenceCalls = _executionFence.AcquisitionCalls;
        var jobCount = (await LoadRecoveryJobsAsync(running.Id)).Count;
        jobCount.ShouldBe(1);
        var inputs = new[]
        {
            input,
            ChangeRecoveryInput(
                CloneRecoveryInput(input),
                value => value.RecoveryRequestId = Guid.NewGuid()),
        };
        foreach (var replayInput in inputs)
        {
            await AssertRecoveryMarkerUnavailableAsync(
                running.Id,
                replayInput,
                ExecutionStatus.OutcomeUnknown,
                residue.ConcurrencyStamp,
                fenceCalls,
                jobCount);
        }

        var pin = await _topologyGuard.CaptureSafeAsync(
            PipelineTerminalStoreTopologyPhase.TerminalFinalize);
        using var topologyScope = _topologyGuard.EnterSafeScope(
            pin,
            PipelineTerminalStoreTopologyPhase.TerminalFinalize);
        foreach (var terminalKind in new[]
                 {
                     "Succeeded",
                     "Failed",
                     "CoordinationFailed",
                     "Cancelled",
                     "OutcomeUnknown",
                 })
        {
            var exception = await Should.ThrowAsync<BusinessException>(() =>
                InvokeLateFinalizeAsync(running.Id, terminalKind));
            exception.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionCoordinationUnavailable);
            var unchanged = await LoadExecutionAsync(running.Id);
            unchanged.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
            unchanged.ConcurrencyStamp.ShouldBe(residue.ConcurrencyStamp);
            _executionFence.AcquisitionCalls.ShouldBe(fenceCalls);
            (await LoadRecoveryJobsAsync(running.Id)).Count.ShouldBe(jobCount);
        }

        _etlRunner.RunCalls.ShouldBe(0);
    }

    [Fact]
    public async Task OutcomeUnknown_Recovery_Marker_Must_Reject_Any_Resolution_Audit_Key()
    {
        var (_, running) = await CreateRecoverableRunningExecutionAsync(
            "Recovery Partial Resolution Audit");
        var input = CreateRecoveryInput(running);
        await _pipelineExecutionAppService.RecoverRunningAsync(
            running.Id,
            input);
        var fenceCalls = _executionFence.AcquisitionCalls;
        var jobCount = (await LoadRecoveryJobsAsync(running.Id)).Count;
        var resolverId = (_currentUser.Id ?? throw new InvalidOperationException(
            "测试登录身份缺少 UserId")).ToString("N");
        var partialAuditValues = new[]
        {
            (
                DataPipelineExecutionCoordinator.ResolutionPropertyName,
                PipelineExecutionResolution.ConfirmedApplied.ToString()),
            (
                DataPipelineExecutionCoordinator.ResolvedAtPropertyName,
                DateTimeOffset.UtcNow.ToString("O")),
            (
                DataPipelineExecutionCoordinator.ResolvedByPropertyName,
                resolverId),
            (
                DataPipelineExecutionCoordinator.VerificationNotePropertyName,
                "目标事务人工复核记录完整"),
        };

        foreach (var (propertyName, value) in partialAuditValues)
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var tracked = await _executionRepository.GetAsync(running.Id);
                tracked.SetProperty(propertyName, value);
                await _executionRepository.UpdateAsync(
                    tracked,
                    autoSave: true);
            });
            var partial = await LoadExecutionAsync(running.Id);
            await AssertRecoveryMarkerUnavailableAsync(
                running.Id,
                input,
                ExecutionStatus.OutcomeUnknown,
                partial.ConcurrencyStamp,
                fenceCalls,
                jobCount);
            await WithUnitOfWorkAsync(async () =>
            {
                var tracked = await _executionRepository.GetAsync(running.Id);
                tracked.ExtraProperties.Remove(propertyName).ShouldBeTrue();
                await _executionRepository.UpdateAsync(
                    tracked,
                    autoSave: true);
            });
        }
    }

    [Fact]
    public async Task Terminal_Recovery_Status_Without_Resolution_Audit_Must_Fail_Closed()
    {
        var (_, running) = await CreateRecoverableRunningExecutionAsync(
            "Recovery Terminal Without Resolution Audit");
        var input = CreateRecoveryInput(running);
        await _pipelineExecutionAppService.RecoverRunningAsync(
            running.Id,
            input);
        var fenceCalls = _executionFence.AcquisitionCalls;
        var jobCount = (await LoadRecoveryJobsAsync(running.Id)).Count;
        var statusProperty = typeof(PipelineExecution)
            .GetProperty(nameof(PipelineExecution.Status))!;

        foreach (var terminalStatus in new[]
                 {
                     ExecutionStatus.Succeeded,
                     ExecutionStatus.Failed,
                 })
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var tracked = await _executionRepository.GetAsync(running.Id);
                statusProperty.SetValue(tracked, terminalStatus);
                await _executionRepository.UpdateAsync(
                    tracked,
                    autoSave: true);
            });
            var terminal = await LoadExecutionAsync(running.Id);
            await AssertRecoveryMarkerUnavailableAsync(
                running.Id,
                input,
                terminalStatus,
                terminal.ConcurrencyStamp,
                fenceCalls,
                jobCount);
            await WithUnitOfWorkAsync(async () =>
            {
                var tracked = await _executionRepository.GetAsync(running.Id);
                statusProperty.SetValue(tracked, ExecutionStatus.OutcomeUnknown);
                await _executionRepository.UpdateAsync(
                    tracked,
                    autoSave: true);
            });
        }
    }

    [Theory]
    [InlineData(
        PipelineExecutionResolution.ConfirmedApplied,
        ExecutionStatus.Succeeded)]
    [InlineData(
        PipelineExecutionResolution.ConfirmedNotApplied,
        ExecutionStatus.Failed)]
    public async Task Resolved_Recovery_Marker_Must_Reject_Each_Missing_Or_Corrupted_Resolution_Audit_Key(
        PipelineExecutionResolution resolution,
        ExecutionStatus expectedStatus)
    {
        var (_, running) = await CreateRecoverableRunningExecutionAsync(
            $"Recovery Resolved Audit {resolution}");
        var input = CreateRecoveryInput(running);
        await _pipelineExecutionAppService.RecoverRunningAsync(
            running.Id,
            input);
        var verificationNote =
            $"incident:RECOVERY-AUDIT-{(int)resolution:D2}";
        var resolved = await _pipelineExecutionAppService.ResolveOutcomeAsync(
            running.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = resolution,
                ProcessedRows = resolution ==
                                PipelineExecutionResolution.ConfirmedApplied
                    ? 31
                    : null,
                VerificationNote = verificationNote,
            });
        resolved.Status.ShouldBe(expectedStatus);

        var resolutionKeys = new[]
        {
            DataPipelineExecutionCoordinator.ResolutionPropertyName,
            DataPipelineExecutionCoordinator.ResolvedAtPropertyName,
            DataPipelineExecutionCoordinator.ResolvedByPropertyName,
            DataPipelineExecutionCoordinator.VerificationNotePropertyName,
        };
        var stored = await LoadExecutionAsync(running.Id);
        var originals = resolutionKeys.ToDictionary(
            key => key,
            key => stored.ExtraProperties[key]);
        var fenceCalls = _executionFence.AcquisitionCalls;
        var jobCount = (await LoadRecoveryJobsAsync(running.Id)).Count;

        foreach (var propertyName in resolutionKeys)
        {
            foreach (var remove in new[] { true, false })
            {
                await WithUnitOfWorkAsync(async () =>
                {
                    var tracked = await _executionRepository.GetAsync(
                        running.Id);
                    if (remove)
                    {
                        tracked.ExtraProperties.Remove(propertyName)
                            .ShouldBeTrue();
                    }
                    else
                    {
                        tracked.SetProperty(
                            propertyName,
                            GetCorruptedResolutionAuditValue(
                                propertyName,
                                expectedStatus));
                    }

                    await _executionRepository.UpdateAsync(
                        tracked,
                        autoSave: true);
                });
                var corrupted = await LoadExecutionAsync(running.Id);
                await AssertRecoveryMarkerUnavailableAsync(
                    running.Id,
                    input,
                    expectedStatus,
                    corrupted.ConcurrencyStamp,
                    fenceCalls,
                    jobCount);
                await WithUnitOfWorkAsync(async () =>
                {
                    var tracked = await _executionRepository.GetAsync(
                        running.Id);
                    tracked.SetProperty(propertyName, originals[propertyName]);
                    await _executionRepository.UpdateAsync(
                        tracked,
                        autoSave: true);
                });
            }
        }
    }

    [Fact]
    public async Task Recovery_Evidence_Must_Use_Structured_Reference_And_Reject_Secrets_Or_Dangerous_Separators()
    {
        var recovererId = _currentUser.Id ?? throw new InvalidOperationException(
            "测试登录身份缺少 UserId");
        var accepted = new[]
        {
            "change:ABCDEFGH",
            "incident:INC-2026-0811",
            "change:CHG_2026_0811",
            "work-order:WO-ETL-0001",
            "dba-evidence:DBA-SESSION_2026-0811",
            $"dba-evidence:{new string('Z', 128)}",
        };
        foreach (var evidence in accepted)
        {
            var (_, running) = await CreateRecoverableRunningExecutionAsync(
                $"Recovery Evidence {evidence[..evidence.IndexOf(':')]} ");
            var input = ChangeRecoveryInput(
                CreateRecoveryInput(running),
                value => value.EvidenceReference = evidence);

            var recovered = await _executionCoordinator.RecoverRunningAsync(
                running.Id,
                input,
                recovererId);

            recovered.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
            ReadExtraString(
                    await LoadExecutionAsync(running.Id),
                    DataPipelineExecutionCoordinator
                        .RecoveryEvidenceReferencePropertyName)
                .ShouldBe(evidence);
            (await LoadRecoveryJobsAsync(running.Id)).Count.ShouldBe(1);
            var resolved = await _pipelineExecutionAppService
                .ResolveOutcomeAsync(
                    running.Id,
                    new ResolvePipelineExecutionOutcomeInput
                    {
                        Resolution = PipelineExecutionResolution
                            .ConfirmedNotApplied,
                        VerificationNote =
                            "incident:RECOVERY-EVIDENCE-RESOLVED",
                    });
            resolved.Status.ShouldBe(ExecutionStatus.Failed,
                "每个合法样本完成后须解除 OutcomeUnknown 目标隔离，避免污染下一个样本");
        }

        var (_, rejectedRunning) =
            await CreateRecoverableRunningExecutionAsync(
                "Recovery Evidence Rejected");
        var rejected = new[]
        {
            "Host=db;Database=SwapMid;Username=etl;Password=secret",
            "SELECT * FROM AppPipelineExecutions",
            "https://evidence.example/case?token=secret",
            "          ",
            "incident:INC/2026",
            "incident:INC:2026",
            "incident:INC=2026",
            "incident:INC;2026",
            "incident:INC?token",
            "incident:INC&token",
            "incident:INC@2026",
            "incident:INC\\2026",
            "incident:INC 2026",
            " incident:INC-2026",
            "incident:INC-2026 ",
            "incident:1234567",
            "dba-evidence:x1",
            "incident:header.payload.signature",
            $"incident:{new string('A', 129)}",
        };
        foreach (var evidence in rejected)
        {
            var input = ChangeRecoveryInput(
                CreateRecoveryInput(rejectedRunning),
                value => value.EvidenceReference = evidence);
            var exception = await Should.ThrowAsync<BusinessException>(() =>
                _executionCoordinator.RecoverRunningAsync(
                    rejectedRunning.Id,
                    input,
                    recovererId));
            exception.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionRecoveryInvalid);
        }

        _executionFence.AcquisitionCalls.ShouldBe(0,
            "不安全 evidence 必须在争 PostgreSQL fence 前拒绝");
        (await LoadExecutionAsync(rejectedRunning.Id)).Status.ShouldBe(
            ExecutionStatus.Running);
        (await LoadRecoveryJobsAsync(rejectedRunning.Id)).ShouldBeEmpty();
        _etlRunner.RunCalls.ShouldBe(0);
    }

    [Fact]
    public async Task RecoverRunning_Should_Honor_PreCancelled_Caller_Before_Taking_Fence()
    {
        var (_, running) = await CreateRecoverableRunningExecutionAsync(
            "Recovery Caller PreCancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _executionCoordinator.RecoverRunningAsync(
                running.Id,
                CreateRecoveryInput(running),
                _currentUser.Id ?? throw new InvalidOperationException(
                    "测试登录身份缺少 UserId"),
                cancellation.Token));

        _executionFence.AcquisitionCalls.ShouldBe(0);
        _etlRunner.RunCalls.ShouldBe(0);
        (await LoadExecutionAsync(running.Id)).Status.ShouldBe(
            ExecutionStatus.Running);
        (await LoadRecoveryJobsAsync(running.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task RecoverRunning_Should_Fail_Closed_When_Redis_Or_PostgreSql_Fence_Is_Busy()
    {
        var (pipeline, running) =
            await CreateRecoverableRunningExecutionAsync(
                "Recovery Busy");
        await using (var heldRedis =
                     await _distributedLock.TryAcquireAsync(
                         DataPipelineExecutionCoordinator
                             .DistributedLockPrefix +
                         pipeline.Id.ToString("N"),
                         TimeSpan.Zero) ??
                     throw new InvalidOperationException(
                         "测试未能预占恢复 Redis 锁"))
        {
            var redisBusy = await Should.ThrowAsync<BusinessException>(() =>
                _pipelineExecutionAppService.RecoverRunningAsync(
                    running.Id,
                    CreateRecoveryInput(running)));
            redisBusy.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionBusy);
            _executionFence.AcquisitionCalls.ShouldBe(0);
            (await LoadExecutionAsync(running.Id)).Status.ShouldBe(
                ExecutionStatus.Running);
        }

        _executionFence.RejectNextAcquisition();
        var fenceBusy = await Should.ThrowAsync<BusinessException>(() =>
            _pipelineExecutionAppService.RecoverRunningAsync(
                running.Id,
                CreateRecoveryInput(running)));
        fenceBusy.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        _executionFence.AcquisitionCalls.ShouldBe(1);
        _etlRunner.RunCalls.ShouldBe(0);
        (await LoadExecutionAsync(running.Id)).Status.ShouldBe(
            ExecutionStatus.Running);
        (await LoadRecoveryJobsAsync(running.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task RecoverRunning_Should_Write_Unknown_Audit_One_Job_And_Replay_Same_Request_After_Resolution()
    {
        var (_, running) = await CreateRecoverableRunningExecutionAsync(
            "Recovery Success");
        var originalTenantId = _currentTenant.Id;
        var tenantId = Guid.NewGuid();
        var input = CreateRecoveryInput(running);
        PipelineExecutionDto recovered;
        var acquisitionCallsBeforeTenantAttempt =
            _executionFence.AcquisitionCalls;

        using (_currentTenant.Change(tenantId, "etl-recovery-tenant"))
        {
            var unauthorized = await Should.ThrowAsync<AbpAuthorizationException>(
                () => _pipelineExecutionAppService.RecoverRunningAsync(
                    running.Id,
                    input));
            unauthorized.Code.ShouldBe(
                SwapMid.Permissions
                    .SwapMidPermissions.DataIntegration
                    .PipelineExecutions.RecoverRunning);
            _currentTenant.Id.ShouldBe(tenantId,
                "拒绝租户入口时不得改变调用方 tenant scope");
        }

        _executionFence.AcquisitionCalls.ShouldBe(
            acquisitionCallsBeforeTenantAttempt,
            "租户恢复请求必须在争 Redis/PG 执行权前拒绝");
        (await LoadExecutionAsync(running.Id)).Status.ShouldBe(
            ExecutionStatus.Running);
        (await LoadRecoveryJobsAsync(running.Id)).ShouldBeEmpty();
        recovered = await _pipelineExecutionAppService
            .RecoverRunningAsync(running.Id, input);

        recovered.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        recovered.ErrorCode.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionOutcomeUnknown);
        recovered.ConcurrencyStamp.ShouldNotBe(running.ConcurrencyStamp);
        _currentTenant.Id.ShouldBe(originalTenantId);
        _executionFence.LastObservedTenantId.ShouldBeNull(
            "恢复双锁与终态事务必须固定使用 host 库");
        _etlRunner.RunCalls.ShouldBe(0,
            "受控恢复只能收敛平台状态，不得重放外部 ETL");

        var stored = await LoadExecutionAsync(running.Id);
        AssertRecoveryAudit(stored, input);
        var jobs = await LoadRecoveryJobsAsync(running.Id);
        jobs.ShouldHaveSingleItem().OutcomeUnknown.ShouldBeTrue();

        var acquisitionCalls = _executionFence.AcquisitionCalls;
        var replay = await _pipelineExecutionAppService.RecoverRunningAsync(
            running.Id,
            input);
        replay.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        _executionFence.AcquisitionCalls.ShouldBe(acquisitionCalls,
            "同 RecoveryRequestId 回放不得再次争锁");
        (await LoadRecoveryJobsAsync(running.Id)).Count.ShouldBe(1);

        var resolved = await _pipelineExecutionAppService.ResolveOutcomeAsync(
            running.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution.ConfirmedApplied,
                ProcessedRows = 17,
                VerificationNote = "incident:RECOVERY-RESOLUTION-0002",
            });
        resolved.Status.ShouldBe(ExecutionStatus.Succeeded);
        acquisitionCalls = _executionFence.AcquisitionCalls;

        var replayAfterResolution = await _pipelineExecutionAppService
            .RecoverRunningAsync(running.Id, input);
        replayAfterResolution.Status.ShouldBe(ExecutionStatus.Succeeded,
            "同一恢复请求在后续人工裁决后仍须幂等返回当前状态");
        replayAfterResolution.ProcessedRows.ShouldBe(17);
        _executionFence.AcquisitionCalls.ShouldBe(acquisitionCalls);
        (await LoadRecoveryJobsAsync(running.Id)).Count.ShouldBe(1);

        var secondRequest = ChangeRecoveryInput(
            CreateRecoveryInput(await LoadExecutionAsync(running.Id)),
            input => input.RecoveryRequestId = Guid.NewGuid());
        var conflict = await Should.ThrowAsync<BusinessException>(() =>
            _pipelineExecutionAppService.RecoverRunningAsync(
                running.Id,
                secondRequest));
        conflict.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionRecoveryConflict);
        _executionFence.AcquisitionCalls.ShouldBe(acquisitionCalls,
            "不同 RecoveryRequestId 必须根据持久 marker 在争锁前冲突");
        (await LoadRecoveryJobsAsync(running.Id)).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("Succeeded")]
    [InlineData("Failed")]
    [InlineData("CoordinationFailed")]
    [InlineData("Cancelled")]
    [InlineData("OutcomeUnknown")]
    public async Task Every_Late_Finalizer_Should_Fail_Closed_After_Running_Recovery_Marker(
        string terminalKind)
    {
        var (_, running) = await CreateRecoverableRunningExecutionAsync(
            $"Recovery Late Finalize {terminalKind}");
        var input = CreateRecoveryInput(running);
        await _pipelineExecutionAppService.RecoverRunningAsync(
            running.Id,
            input);
        var jobsBefore = await LoadRecoveryJobsAsync(running.Id);
        jobsBefore.Count.ShouldBe(1);

        var pin = await _topologyGuard.CaptureSafeAsync(
            PipelineTerminalStoreTopologyPhase.TerminalFinalize);
        using var topologyScope = _topologyGuard.EnterSafeScope(
            pin,
            PipelineTerminalStoreTopologyPhase.TerminalFinalize);
        var exception = await Should.ThrowAsync<BusinessException>(() =>
            InvokeLateFinalizeAsync(running.Id, terminalKind));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        var stored = await LoadExecutionAsync(running.Id);
        stored.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        AssertRecoveryAudit(stored, input);
        (await LoadRecoveryJobsAsync(running.Id)).Count.ShouldBe(1,
            "迟到 owner 不得覆盖恢复 marker，也不得重复入队");
        _etlRunner.RunCalls.ShouldBe(0);
    }

    [Fact]
    public async Task ResolveOutcome_Should_Reject_Tenant_Context_Before_Fence()
    {
        var unknown = await CreateUnknownExecutionAsync(
            "Resolve Tenant Forbidden");
        var acquisitionCalls = _executionFence.AcquisitionCalls;
        var tenantId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId, "etl-resolve-tenant"))
        {
            var unauthorized = await Should.ThrowAsync<AbpAuthorizationException>(
                () => _pipelineExecutionAppService.ResolveOutcomeAsync(
                    unknown.Id,
                    new ResolvePipelineExecutionOutcomeInput
                    {
                        Resolution =
                            PipelineExecutionResolution.ConfirmedApplied,
                        ProcessedRows = 1,
                        VerificationNote =
                            "incident:TENANT-RESOLUTION-DENIED-0001",
                    }));
            unauthorized.Code.ShouldBe(
                SwapMid.Permissions
                    .SwapMidPermissions.DataIntegration
                    .PipelineExecutions.ResolveOutcome);
            _currentTenant.Id.ShouldBe(tenantId);
        }

        _executionFence.AcquisitionCalls.ShouldBe(
            acquisitionCalls,
            "租户裁决请求必须在争 PostgreSQL fence 前拒绝");
        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(unknown.Id));
        stored.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        stored.ExtraProperties.ContainsKey(
                DataPipelineExecutionCoordinator.ResolutionPropertyName)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmedApplied_Should_Record_Audit_And_Replay_Same_Resolution_Idempotently()
    {
        var unknown = await CreateUnknownExecutionAsync("Resolve Applied");
        var resolved = await _pipelineExecutionAppService.ResolveOutcomeAsync(
            unknown.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution.ConfirmedApplied,
                ProcessedRows = 41,
                VerificationNote = "incident:RESOLVE-APPLIED-0001",
            });

        resolved.Status.ShouldBe(ExecutionStatus.Succeeded);
        resolved.ProcessedRows.ShouldBe(41);
        resolved.ErrorCode.ShouldBeNull();
        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(unknown.Id));
        AssertResolutionAudit(
            stored,
            PipelineExecutionResolution.ConfirmedApplied,
            "incident:RESOLVE-APPLIED-0001");
        JsonSerializer.Serialize(stored.ExtraProperties).ShouldNotContain(
            "ETL target commit outcome is unknown.");
        var resolvedAt = ReadExtraString(
            stored,
            DataPipelineExecutionCoordinator.ResolvedAtPropertyName);
        var acquisitionCalls = _executionFence.AcquisitionCalls;

        var replay = await _pipelineExecutionAppService.ResolveOutcomeAsync(
            unknown.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution.ConfirmedApplied,
                ProcessedRows = 999,
                VerificationNote = "incident:RESOLVE-APPLIED-REPLAY-0002",
            });

        replay.Status.ShouldBe(ExecutionStatus.Succeeded);
        replay.ProcessedRows.ShouldBe(41);
        _executionFence.AcquisitionCalls.ShouldBe(acquisitionCalls);
        var replayStored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(unknown.Id));
        ReadExtraString(
                replayStored,
                DataPipelineExecutionCoordinator.ResolvedAtPropertyName)
            .ShouldBe(resolvedAt);
        ReadExtraString(
                replayStored,
                DataPipelineExecutionCoordinator.VerificationNotePropertyName)
            .ShouldBe("incident:RESOLVE-APPLIED-0001");

        var conflict = await Should.ThrowAsync<BusinessException>(() =>
            _pipelineExecutionAppService.ResolveOutcomeAsync(
                unknown.Id,
                new ResolvePipelineExecutionOutcomeInput
                {
                    Resolution = PipelineExecutionResolution.ConfirmedNotApplied,
                    VerificationNote = "incident:RESOLVE-CONFLICT-0001",
                }));
        conflict.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionResolutionInvalid);
    }

    [Fact]
    public async Task ManualV0旧自由文本应继续收敛但正文不得进入持久证据()
    {
        var unknown = await CreateUnknownExecutionAsync(
            "Resolve Legacy Note Compatibility");
        const string legacyNote =
            "Server=db;Database=SwapMid;Password=legacy-secret";

        var resolved = await _pipelineExecutionAppService.ResolveOutcomeAsync(
            unknown.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution.ConfirmedApplied,
                ProcessedRows = 3,
                VerificationNote = legacyNote,
            });

        resolved.Status.ShouldBe(ExecutionStatus.Succeeded);
        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(unknown.Id));
        var storedReference = ReadExtraString(
            stored,
            DataPipelineExecutionCoordinator.VerificationNotePropertyName);
        storedReference.ShouldStartWith(
            "dba-evidence:legacy-redacted-");
        storedReference.ShouldNotContain("legacy-secret");
        JsonSerializer.Serialize(stored.ExtraProperties)
            .ShouldNotContain(legacyNote);
    }

    [Fact]
    public async Task ManualV0旧未应用说明应继续收敛但正文不得进入通知证据()
    {
        var unknown = await CreateUnknownExecutionAsync(
            "Resolve Legacy Not Applied Compatibility");
        const string legacyNote =
            "DBA confirmed the target transaction was not applied";

        var resolved = await _pipelineExecutionAppService.ResolveOutcomeAsync(
            unknown.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution.ConfirmedNotApplied,
                VerificationNote = legacyNote,
            });

        resolved.Status.ShouldBe(ExecutionStatus.Failed);
        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(unknown.Id));
        var storedReference = ReadExtraString(
            stored,
            DataPipelineExecutionCoordinator.VerificationNotePropertyName);
        storedReference.ShouldStartWith(
            "dba-evidence:legacy-redacted-");
        storedReference.ShouldNotContain(legacyNote);
        JsonSerializer.Serialize(stored.ExtraProperties)
            .ShouldNotContain(legacyNote);
    }

    [Fact]
    public async Task ConfirmedApplied_Should_Not_Be_Blocked_By_Notification_Store_Topology()
    {
        var unknown = await CreateUnknownExecutionAsync(
            "Resolve Applied Without Notification Store");
        _topologyGuard.Reset();
        _topologyGuard.RejectAt(
            PipelineTerminalStoreTopologyPhase.OutcomeResolution);
        _executionFence.Reset();

        var resolved = await _pipelineExecutionAppService.ResolveOutcomeAsync(
            unknown.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution.ConfirmedApplied,
                ProcessedRows = 3,
                VerificationNote = "incident:RESOLVE-NO-NOTIFY-0001",
            });

        resolved.Status.ShouldBe(ExecutionStatus.Succeeded);
        resolved.ProcessedRows.ShouldBe(3);
        _topologyGuard.Calls.Count.ShouldBe(4,
            "Capture、Enter、pin 内重读与终态写事务都必须经过 Applied phase");
        _topologyGuard.Calls.ShouldAllBe(phase =>
            phase == PipelineTerminalStoreTopologyPhase
                .OutcomeResolutionApplied);
        _executionFence.AcquisitionCalls.ShouldBe(1);
    }

    [Fact]
    public async Task ConfirmedApplied_Should_Pin_Original_Default_Across_Config_Hot_Change()
    {
        const string capturedDefault = "sqlite:resolve-applied-before";
        const string changedDefault = "sqlite:resolve-applied-after";
        var unknown = await CreateUnknownExecutionAsync(
            "Resolve Applied Topology Pin");
        _topologyGuard.Reset();
        _topologyGuard.ConfiguredDefault = capturedDefault;
        _executionFence.Reset();
        _executionFence.AcquisitionCallback = () =>
            _topologyGuard.ConfiguredDefault = changedDefault;

        var resolved = await _pipelineExecutionAppService.ResolveOutcomeAsync(
            unknown.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution.ConfirmedApplied,
                ProcessedRows = 5,
                VerificationNote = "incident:RESOLVE-PINNED-DEFAULT-0001",
            });

        resolved.Status.ShouldBe(ExecutionStatus.Succeeded);
        _topologyGuard.SafeScopeDefaults.ShouldBe(new[]
        {
            capturedDefault,
            capturedDefault,
        }, "配置在 fence 获取时热切后，读取与终态写事务都必须使用捕获值");
        _topologyGuard.ConfiguredDefault.ShouldBe(changedDefault);
        _topologyGuard.IsScopeActive.ShouldBeFalse(
            "人工裁决退出时必须撤销 topology scope");
        _topologyGuard.CurrentPinnedDefault.ShouldBeNull();
    }

    [Fact]
    public async Task ConfirmedNotApplied_Should_Reject_Topology_Before_Fence_And_Keep_Unknown()
    {
        var unknown = await CreateUnknownExecutionAsync(
            "Resolve Not Applied Topology Failure");
        _topologyGuard.Reset();
        _topologyGuard.RejectAt(
            PipelineTerminalStoreTopologyPhase.OutcomeResolution);
        _executionFence.Reset();

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _pipelineExecutionAppService.ResolveOutcomeAsync(
                unknown.Id,
                new ResolvePipelineExecutionOutcomeInput
                {
                    Resolution =
                        PipelineExecutionResolution.ConfirmedNotApplied,
                    VerificationNote = "incident:RESOLVE-TOPOLOGY-0001",
                }));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        _topologyGuard.Calls.ShouldContain(
            PipelineTerminalStoreTopologyPhase.OutcomeResolution);
        _executionFence.AcquisitionCalls.ShouldBe(0);
        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(unknown.Id));
        stored.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        stored.ExtraProperties.ContainsKey(
                DataPipelineExecutionCoordinator.ResolutionPropertyName)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmedNotApplied_Should_Record_Audit_And_Replay_Same_Resolution_Idempotently()
    {
        var unknown = await CreateUnknownExecutionAsync("Resolve Not Applied");
        var resolved = await _pipelineExecutionAppService.ResolveOutcomeAsync(
            unknown.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution.ConfirmedNotApplied,
                VerificationNote = "incident:RESOLVE-NOT-APPLIED-0001",
            });

        resolved.Status.ShouldBe(ExecutionStatus.Failed);
        resolved.ProcessedRows.ShouldBeNull();
        resolved.ErrorCode.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReconciledNotApplied);
        var stored = await WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(unknown.Id));
        AssertResolutionAudit(
            stored,
            PipelineExecutionResolution.ConfirmedNotApplied,
            "incident:RESOLVE-NOT-APPLIED-0001");
        ReadExtraString(
                stored,
                PipelineExecutionFailurePolicy.ErrorCodePropertyName)
            .ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionReconciledNotApplied);
        JsonSerializer.Serialize(stored.ExtraProperties).ShouldNotContain(
            "ETL target commit outcome is unknown.");
        var acquisitionCalls = _executionFence.AcquisitionCalls;

        var replay = await _pipelineExecutionAppService.ResolveOutcomeAsync(
            unknown.Id,
            new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution.ConfirmedNotApplied,
                VerificationNote = "incident:RESOLVE-NOT-APPLIED-REPLAY-0002",
            });

        replay.Status.ShouldBe(ExecutionStatus.Failed);
        replay.ErrorCode.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReconciledNotApplied);
        _executionFence.AcquisitionCalls.ShouldBe(acquisitionCalls);

        var conflict = await Should.ThrowAsync<BusinessException>(() =>
            _pipelineExecutionAppService.ResolveOutcomeAsync(
                unknown.Id,
                new ResolvePipelineExecutionOutcomeInput
                {
                    Resolution = PipelineExecutionResolution.ConfirmedApplied,
                    ProcessedRows = 1,
                    VerificationNote = "incident:RESOLVE-CONFLICT-0002",
                }));
        conflict.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionResolutionInvalid);
    }

    [Fact]
    public async Task ResolveOutcome_Should_Reject_Non_Unknown_Execution()
    {
        var pipeline = await CreateActivePipelineAsync("Resolve Known State");
        var succeeded = await _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            Guid.NewGuid());

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _pipelineExecutionAppService.ResolveOutcomeAsync(
                succeeded.Id,
                new ResolvePipelineExecutionOutcomeInput
                {
                    Resolution = PipelineExecutionResolution.ConfirmedApplied,
                    ProcessedRows = 0,
                    VerificationNote = "incident:RESOLVE-TERMINAL-0001",
                }));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionResolutionInvalid);
    }

    private async Task<(DataPipelineDto Pipeline, PipelineExecution Running)>
        CreateRecoverableRunningExecutionAsync(
            string prefix)
    {
        var pipeline = await CreateActivePipelineAsync(prefix);
        var completed = await _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            Guid.NewGuid());
        var completedStored = await LoadExecutionAsync(completed.Id);
        var running = new PipelineExecution(
            Guid.NewGuid(),
            pipeline.Id,
            pipeline.Name,
            TriggerType.Manual,
            completedStored.TargetResourceHash,
            completedStored.VerifiedTargetResourceHash,
            completedStored.EvidenceProtocol,
            completedStored.TargetResourceInstanceId,
            completedStored.ExecutionContractHash,
            completedStored.TargetCoordinationIdentityHash);
        typeof(PipelineExecution)
            .GetProperty(nameof(PipelineExecution.StartedAt))!
            .SetValue(running, DateTime.UtcNow.AddMinutes(-10));
        foreach (var property in completedStored.ExtraProperties)
        {
            running.SetProperty(property.Key, property.Value);
        }

        await WithUnitOfWorkAsync(() =>
            _executionRepository.InsertAsync(running, autoSave: true));
        _etlRunner.Reset();
        _executionFence.Reset();
        _topologyGuard.Reset();
        return (pipeline, await LoadExecutionAsync(running.Id));
    }

    private static RecoverPipelineExecutionInput CreateRecoveryInput(
        PipelineExecution execution,
        DateTimeOffset? now = null)
    {
        var observedAt = now ?? DateTimeOffset.UtcNow;
        return new RecoverPipelineExecutionInput
        {
            RecoveryRequestId = Guid.NewGuid(),
            ExpectedConcurrencyStamp = execution.ConcurrencyStamp,
            DrainStartedAt = observedAt.AddMinutes(-8),
            OldOwnerExitVerifiedAt = observedAt.AddMinutes(-7),
            TargetSessionExitVerifiedAt = observedAt.AddMinutes(-6),
            EvidenceReference =
                "incident:INC-2026-ETL-RUNNING-RECOVERY",
        };
    }

    private static RecoverPipelineExecutionInput CloneRecoveryInput(
        RecoverPipelineExecutionInput input)
        => new()
        {
            RecoveryRequestId = input.RecoveryRequestId,
            ExpectedConcurrencyStamp = input.ExpectedConcurrencyStamp,
            DrainStartedAt = input.DrainStartedAt,
            OldOwnerExitVerifiedAt = input.OldOwnerExitVerifiedAt,
            TargetSessionExitVerifiedAt = input.TargetSessionExitVerifiedAt,
            EvidenceReference = input.EvidenceReference,
        };

    private static RecoverPipelineExecutionInput ChangeRecoveryInput(
        RecoverPipelineExecutionInput input,
        Action<RecoverPipelineExecutionInput> change)
    {
        change(input);
        return input;
    }

    private async Task AssertRecoveryReplayMismatchesAsync(
        Guid executionId,
        RecoverPipelineExecutionInput originalInput,
        IReadOnlyCollection<RecoveryReplayAttempt> mismatches,
        ExecutionStatus expectedStatus)
    {
        var baseline = await LoadExecutionAsync(executionId);
        baseline.Status.ShouldBe(expectedStatus);
        var baselineStamp = baseline.ConcurrencyStamp;
        var baselineJobs = await LoadRecoveryJobsAsync(executionId);
        var baselineFenceCalls = _executionFence.AcquisitionCalls;

        foreach (var mismatch in mismatches)
        {
            var exception = await Should.ThrowAsync<BusinessException>(() =>
                _executionCoordinator.RecoverRunningAsync(
                    executionId,
                    mismatch.Input,
                    mismatch.RecovererId));

            exception.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionRecoveryConflict);
            _executionFence.AcquisitionCalls.ShouldBe(
                baselineFenceCalls,
                "同 requestId 的不同证明载荷必须在争锁前拒绝");
            var stored = await LoadExecutionAsync(executionId);
            stored.Status.ShouldBe(expectedStatus);
            stored.ConcurrencyStamp.ShouldBe(
                baselineStamp,
                "冲突回放不得产生任何持久化写入");
            AssertRecoveryAudit(stored, originalInput);
            (await LoadRecoveryJobsAsync(executionId)).Count.ShouldBe(
                baselineJobs.Count,
                "冲突回放不得重复通知任务");
            _etlRunner.RunCalls.ShouldBe(0);
        }
    }

    private async Task AssertRecoveryMarkerUnavailableAsync(
        Guid executionId,
        RecoverPipelineExecutionInput input,
        ExecutionStatus expectedStatus,
        string expectedConcurrencyStamp,
        int expectedFenceCalls,
        int expectedJobCount)
    {
        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _pipelineExecutionAppService.RecoverRunningAsync(
                executionId,
                input));
        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        _executionFence.AcquisitionCalls.ShouldBe(
            expectedFenceCalls,
            "不完整恢复/裁决 marker 必须在争 PostgreSQL fence 前失败关闭");
        var unchanged = await LoadExecutionAsync(executionId);
        unchanged.Status.ShouldBe(expectedStatus);
        unchanged.ConcurrencyStamp.ShouldBe(
            expectedConcurrencyStamp,
            "marker 完整性失败不得产生持久化写入");
        (await LoadRecoveryJobsAsync(executionId)).Count.ShouldBe(
            expectedJobCount,
            "marker 完整性失败不得新增通知任务");
        _etlRunner.RunCalls.ShouldBe(0);
    }

    private static string GetCorruptedResolutionAuditValue(
        string propertyName,
        ExecutionStatus expectedStatus)
        => propertyName switch
        {
            DataPipelineExecutionCoordinator.ResolutionPropertyName =>
                (expectedStatus == ExecutionStatus.Succeeded
                    ? PipelineExecutionResolution.ConfirmedNotApplied
                    : PipelineExecutionResolution.ConfirmedApplied)
                .ToString(),
            DataPipelineExecutionCoordinator.ResolvedAtPropertyName =>
                "not-a-timestamp",
            DataPipelineExecutionCoordinator.ResolvedByPropertyName =>
                Guid.Empty.ToString("N"),
            DataPipelineExecutionCoordinator.VerificationNotePropertyName =>
                " short ",
            _ => throw new ArgumentOutOfRangeException(
                nameof(propertyName),
                propertyName,
                null),
        };

    private Task<PipelineExecution> LoadExecutionAsync(Guid executionId)
        => WithUnitOfWorkAsync(
            () => _executionRepository.GetAsync(executionId));

    private async Task<List<PipelineFailureNotificationJobArgs>>
        LoadRecoveryJobsAsync(Guid executionId)
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

    private void AssertRecoveryAudit(
        PipelineExecution execution,
        RecoverPipelineExecutionInput input)
    {
        ReadExtraString(
                execution,
                DataPipelineExecutionCoordinator
                    .RecoveryRequestIdPropertyName)
            .ShouldBe(input.RecoveryRequestId.ToString("N"));
        ReadExtraString(
                execution,
                DataPipelineExecutionCoordinator.RecoveredAtPropertyName)
            .ShouldNotBeNullOrWhiteSpace();
        ReadExtraString(
                execution,
                DataPipelineExecutionCoordinator.RecoveredByPropertyName)
            .ShouldBe((_currentUser.Id ?? throw new InvalidOperationException(
                "测试登录身份缺少 UserId")).ToString("N"));
        ReadExtraString(
                execution,
                DataPipelineExecutionCoordinator
                    .RecoveryExpectedConcurrencyStampPropertyName)
            .ShouldBe(input.ExpectedConcurrencyStamp);
        ReadExtraTimestamp(
                execution,
                DataPipelineExecutionCoordinator
                    .RecoveryDrainStartedAtPropertyName)
            .ShouldBe(input.DrainStartedAt.ToUniversalTime());
        ReadExtraTimestamp(
                execution,
                DataPipelineExecutionCoordinator
                    .RecoveryOldOwnerExitVerifiedAtPropertyName)
            .ShouldBe(input.OldOwnerExitVerifiedAt.ToUniversalTime());
        ReadExtraTimestamp(
                execution,
                DataPipelineExecutionCoordinator
                    .RecoveryTargetSessionExitVerifiedAtPropertyName)
            .ShouldBe(input.TargetSessionExitVerifiedAt.ToUniversalTime());
        ReadExtraString(
                execution,
                DataPipelineExecutionCoordinator
                    .RecoveryEvidenceReferencePropertyName)
            .ShouldBe(input.EvidenceReference);
        ReadExtraString(
                execution,
                DataPipelineExecutionCoordinator
                    .RecoveryProtocolVersionPropertyName)
            .ShouldBe("1");
    }

    private async Task<PipelineExecution> InvokeLateFinalizeAsync(
        Guid executionId,
        string terminalKindName)
    {
        var method = typeof(DataPipelineExecutionCoordinator).GetMethod(
                         "FinalizeCoreAsync",
                         BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException(
                         "FinalizeCoreAsync test seam not found");
        var terminalKind = Enum.Parse(
            method.GetParameters()[1].ParameterType,
            terminalKindName);
        var processedRows = string.Equals(
            terminalKindName,
            "Succeeded",
            StringComparison.Ordinal)
            ? 99L
            : (long?)null;
        var task = method.Invoke(
                       _executionCoordinator,
                       new object?[]
                        {
                            executionId,
                            terminalKind,
                            processedRows,
                            null,
                            null,
                        }) as Task<PipelineExecution>
                   ?? throw new InvalidOperationException(
                       "FinalizeCoreAsync returned unexpected task type");
        return await task;
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
            string text => DateTimeOffset.Parse(text).ToUniversalTime(),
            JsonElement { ValueKind: JsonValueKind.String } json =>
                DateTimeOffset.Parse(json.GetString()!).ToUniversalTime(),
            _ => null,
        };
    }

    private async Task<DataPipelineDto> CreateActivePipelineAsync(string prefix)
    {
        var source = await CreateSourceAsync($"{prefix} Source");
        var target = await CreateSourceAsync($"{prefix} Target");
        return await CreateActivePipelineAsync(
            prefix,
            source.Id,
            target.Id);
    }

    private async Task<DataPipelineDto> CreateActivePipelineAsync(
        string prefix,
        Guid sourceId,
        Guid targetId,
        string sourceQuery = "SELECT 1 AS Id")
    {
        var pipeline = await _dataPipelineAppService.CreateAsync(new DataPipelineCreateUpdateDto
        {
            Name = $"{prefix} Pipeline {Guid.NewGuid():N}",
            SourceId = sourceId,
            TargetId = targetId,
            TransformConfig = CreateExecutableTransformConfig(sourceQuery),
        });
        await _dataPipelineAppService.ActivateAsync(pipeline.Id);
        return pipeline;
    }

    private static string CreateExecutableTransformConfig(string sourceQuery)
        => JsonSerializer.Serialize(new
        {
            sourceQuery,
            targetTable = "dbo.Target",
            mode = "append",
        });

    private async Task<PipelineExecutionDto> CreateUnknownExecutionAsync(
        string prefix)
    {
        var pipeline = await CreateActivePipelineAsync(prefix);
        _etlRunner.ExceptionToThrow =
            new EtlTargetCommitOutcomeUnknownException();
        return await _dataPipelineAppService.ExecuteAsync(
            pipeline.Id,
            Guid.NewGuid());
    }

    private Task<DataSourceDto> CreateSourceAsync(
        string prefix,
        string? targetResourceGroup = null,
        string host = "localhost",
        bool includeTargetResourceGroup = true)
    {
        var config = includeTargetResourceGroup
            ? JsonSerializer.Serialize(new
            {
                provider = "sqlserver",
                host,
                port = 1433,
                database = "SwapMid",
                username = "reader",
                targetResourceGroup = targetResourceGroup ??
                    CreateTestTargetResourceGroup(prefix),
            })
            : JsonSerializer.Serialize(new
            {
                provider = "sqlserver",
                host,
                port = 1433,
                database = "SwapMid",
                username = "reader",
            });
        return _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"{prefix} {Guid.NewGuid():N}",
            Type = DataSourceType.RelationalDatabase,
            Config = config,
        });
    }

    private static string CreateTestTargetResourceGroup(string prefix)
    {
        var slug = new string(prefix
            .ToLowerInvariant()
            .Select(character =>
                character is >= 'a' and <= 'z' or >= '0' and <= '9'
                    ? character
                    : '-')
            .ToArray())
            .Trim('-');
        return $"test:{slug}";
    }

    private static void AssertSafeFailure(
        PipelineExecutionDto execution,
        Type exceptionType,
        params string[] secrets)
    {
        execution.Status.ShouldBe(ExecutionStatus.Failed);
        execution.ErrorCode.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed);
        execution.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
        execution.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        execution.ExceptionType.ShouldBe(exceptionType.FullName);
        AssertDoesNotContainSecrets(execution, secrets);
    }

    private static void AssertLegacyFailureIsSafe(
        PipelineExecutionDto execution,
        params string[] secrets)
    {
        execution.Status.ShouldBe(ExecutionStatus.Failed);
        execution.ErrorCode.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed);
        execution.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
        execution.CorrelationId.ShouldBeNull();
        execution.ExceptionType.ShouldBeNull();
        AssertDoesNotContainSecrets(execution, secrets);
    }

    private static void AssertDoesNotContainSecrets(
        object value,
        params string[] secrets)
    {
        var serialized = JsonSerializer.Serialize(value);
        foreach (var secret in secrets)
        {
            serialized.ShouldNotContain(secret);
        }
    }

    private void AssertResolutionAudit(
        PipelineExecution execution,
        PipelineExecutionResolution resolution,
        string verificationNote)
    {
        ReadExtraString(
                execution,
                DataPipelineExecutionCoordinator.ResolutionPropertyName)
            .ShouldBe(resolution.ToString());
        ReadExtraString(
                execution,
                DataPipelineExecutionCoordinator.ResolvedAtPropertyName)
            .ShouldNotBeNullOrWhiteSpace();
        ReadExtraString(
                execution,
                DataPipelineExecutionCoordinator.ResolvedByPropertyName)
            .ShouldBe((_currentUser.Id ?? throw new InvalidOperationException(
                "测试登录身份缺少 UserId")).ToString("N"));
        ReadExtraString(
                execution,
                DataPipelineExecutionCoordinator.VerificationNotePropertyName)
            .ShouldBe(verificationNote);
    }

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
            _ => value?.ToString(),
        };
    }

    private sealed record RecoveryReplayAttempt(
        RecoverPipelineExecutionInput Input,
        Guid RecovererId);
}
