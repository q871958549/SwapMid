using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>
/// ETL 失败终态与 ABP 后台通知的真实 PostgreSQL 事务契约。
/// 共享 EF 测试模块会全局关闭 UoW 事务，只能证明两边最终存在，不能证明跨 DbContext 原子性。
/// 数据集成实体按 ABP 语义固定使用 host Default；契约还验证一次执行捕获连接后，
/// host 配置热变也不能把 Platform 与 BackgroundJobs 拆到两个事务。
/// </summary>
[Collection(DataPipelineExecutionPostgreSqlContractCollection.Name)]
public sealed class DataPipelineExecutionPostgreSqlTransactionContractTests
{
    private const string PostgreSqlImage =
        "postgres:18.4@sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a";
    private const string DriftDatabaseName = "swapmid_etl_uow_drift";

    private static readonly JsonSerializerOptions JobArgsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [PostgreSqlContractFact]
    [Trait("Category", "PostgreSqlContract")]
    public async Task 失败终态与通知任务应原子提交_且提交故障时共同回滚()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        SwapMidEfCoreEntityExtensionMappings.Configure();

        await using var postgres = new PostgreSqlBuilder(PostgreSqlImage)
            .WithDatabase("swapmid_etl_uow_contract")
            .WithUsername("swapmid_etl_uow_contract")
            .WithPassword("SwapMidEtlUowContractOnly_2026")
            .Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var driftConnectionString = await CreateDriftDatabaseAsync(
            connectionString);
        await CreateRequiredSchemasAsync(connectionString);
        await CreateRequiredSchemasAsync(driftConnectionString);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
            })
            .Build();

        using var application = await AbpApplicationFactory.CreateAsync<
            DataPipelineExecutionPostgreSqlContractTestModule>(options =>
        {
            options.Services.ReplaceConfiguration(configuration);
            options.UseAutofac();
        });

        await application.InitializeAsync();
        try
        {
            await AssertSharedConnectionStringAsync(
                application.ServiceProvider);
            await AssertSharedDatabaseTransactionAsync(
                application.ServiceProvider);
            await AssertForUpdateRequiresTransactionalUnitOfWorkAsync(
                application.ServiceProvider);
            await AssertPinnedTransactionSurvivesConfigurationDriftAsync(
                application.ServiceProvider,
                connectionString,
                driftConnectionString);
            var runner = application.ServiceProvider
                .GetRequiredService<IEtlPipelineRunner>()
                .ShouldBeOfType<FakeEtlPipelineRunner>();

            await VerifyNormalAtomicCommitAsync(
                application.ServiceProvider,
                connectionString,
                runner);
            await VerifyCommitAcknowledgementLossAsync(
                application.ServiceProvider,
                connectionString,
                runner);
            await VerifyAtomicRollbackAsync(
                application.ServiceProvider,
                connectionString,
                runner);
            await VerifyRecoveryForUpdateSerializesCompetingWritersAsync(
                application.ServiceProvider,
                runner);
            await VerifyCompetingRecoveryRequestsHaveSingleWinnerAsync(
                application.ServiceProvider,
                connectionString,
                runner);
            await VerifyRecoveryRejectsStaleAbpConcurrencyStampAsync(
                application.ServiceProvider,
                runner);
            await VerifyRecoveryFenceLeaseLossRollsBackAsync(
                application.ServiceProvider,
                connectionString,
                runner);
            await VerifyRecoveryCommitAcknowledgementLossAsync(
                application.ServiceProvider,
                connectionString,
                runner);
            await VerifyRecoveryCommitAcknowledgementAndOwnershipLossAsync(
                application.ServiceProvider,
                connectionString,
                runner);
            await VerifyRecoveryDeferredJobFaultRollsBackAsync(
                application.ServiceProvider,
                connectionString,
                runner);
        }
        finally
        {
            EtlTerminalCommitAckFaultInterceptor.Instance.Reset();
            await application.ShutdownAsync();
        }
    }

    private static async Task AssertSharedConnectionStringAsync(
        IServiceProvider root)
    {
        using var scope = root.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<
            IConnectionStringResolver>();
        resolver.ShouldBeOfType<PipelinePinnedConnectionStringResolver>(
            "真实 PostgreSQL 契约不得用测试 resolver 绕过执行级连接 pin");
        scope.ServiceProvider.GetRequiredService<
                IPipelineTerminalUnitOfWorkGuard>()
            .ShouldBeOfType<PipelineTerminalUnitOfWorkGuard>(
                "真实 PostgreSQL 契约不得绕过事务型终态 UoW 守卫");
        var platformConnectionString = await resolver.ResolveAsync(
            ConnectionStrings.DefaultConnectionStringName);
        var backgroundJobsConnectionString = await resolver.ResolveAsync(
            AbpBackgroundJobsDbProperties.ConnectionStringName);

        string.Equals(
                platformConnectionString,
                backgroundJobsConnectionString,
                StringComparison.Ordinal)
            .ShouldBeTrue(
                "SwapMidDbContext 与 BackgroundJobsDbContext 必须解析到同一原始连接串，ABP 才会复用事务");
        scope.ServiceProvider.GetRequiredService<
                IOptions<AbpBackgroundJobOptions>>()
            .Value.IsJobExecutionEnabled.ShouldBeFalse(
                "契约期间后台 worker 不得抢走待断言的通知任务");
    }

    private static async Task AssertSharedDatabaseTransactionAsync(
        IServiceProvider root)
    {
        using var scope = root.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<
            IUnitOfWorkManager>();
        using var unitOfWork = manager.Begin(
            requiresNew: true,
            isTransactional: true);

        var platform = await scope.ServiceProvider
            .GetRequiredService<IDbContextProvider<SwapMidDbContext>>()
            .GetDbContextAsync();
        var backgroundJobs = (DbContext)await scope.ServiceProvider
            .GetRequiredService<IDbContextProvider<IBackgroundJobsDbContext>>()
            .GetDbContextAsync();
        var platformTransaction = platform.Database.CurrentTransaction ??
                                  throw new InvalidOperationException(
                                      "SwapMidDbContext 未进入真实事务");
        var backgroundJobsTransaction =
            backgroundJobs.Database.CurrentTransaction ??
            throw new InvalidOperationException(
                "BackgroundJobsDbContext 未进入真实事务");

        ReferenceEquals(
                platform.Database.GetDbConnection(),
                backgroundJobs.Database.GetDbConnection())
            .ShouldBeTrue("两个 DbContext 必须复用同一物理连接");
        ReferenceEquals(
                platformTransaction.GetDbTransaction(),
                backgroundJobsTransaction.GetDbTransaction())
            .ShouldBeTrue("两个 DbContext 必须复用同一物理事务");
        (await ReadTransactionIdAsync(platform))
            .ShouldBe(await ReadTransactionIdAsync(backgroundJobs));

        await unitOfWork.RollbackAsync();
    }

    private static async Task AssertForUpdateRequiresTransactionalUnitOfWorkAsync(
        IServiceProvider root)
    {
        using var scope = root.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<
            IUnitOfWorkManager>();
        using var unitOfWork = manager.Begin(
            requiresNew: true,
            isTransactional: false);
        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<
                    IPipelineExecutionRepository>()
                .GetForUpdateAsync(Guid.NewGuid()));
        exception.Message.ToLowerInvariant().ShouldContain("transaction");
        await unitOfWork.RollbackAsync();
    }

    private static async Task AssertPinnedTransactionSurvivesConfigurationDriftAsync(
        IServiceProvider root,
        string originalConnectionString,
        string driftConnectionString)
    {
        var pipelineId = await CreateActivePipelineAsync(
            root,
            "topology-drift");
        using var scope = root.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<
            ICurrentTenant>();
        using var tenantScope = currentTenant.Change(
            Guid.NewGuid(),
            "etl-topology-contract-tenant");
        var guard = scope.ServiceProvider.GetRequiredService<
            IPipelineTerminalStoreTopologyGuard>();
        var pin = await guard.CaptureSafeAsync(
            PipelineTerminalStoreTopologyPhase.ExecutionAdmission);
        using var topologyScope = guard.EnterSafeScope(
            pin,
            PipelineTerminalStoreTopologyPhase.ExecutionAdmission);

        var options = scope.ServiceProvider.GetRequiredService<
                IOptionsMonitor<AbpDbConnectionOptions>>()
            .CurrentValue;
        var originalDefault = options.ConnectionStrings.Default;
        var jobsName = AbpBackgroundJobsDbProperties.ConnectionStringName;
        var hadExplicitJobs = options.ConnectionStrings.TryGetValue(
            jobsName,
            out var originalJobs);
        var executionId = Guid.NewGuid();

        try
        {
            var manager = scope.ServiceProvider.GetRequiredService<
                IUnitOfWorkManager>();
            using var unitOfWork = manager.Begin(
                requiresNew: true,
                isTransactional: true);
            var execution = new PipelineExecution(
                executionId,
                pipelineId,
                "pg-topology-drift-contract",
                TriggerType.Manual);
            var failure = new PipelineExecutionFailurePolicy
                .FailureDescriptor(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionFailed,
                    "ETL contract failure",
                    "pg-topology-drift",
                    "ContractFailure");
            scope.ServiceProvider.GetRequiredService<
                    PipelineExecutionFailurePolicy>()
                .Record(execution, failure, DateTime.UtcNow);
            await scope.ServiceProvider.GetRequiredService<
                    IRepository<PipelineExecution, Guid>>()
                .InsertAsync(execution, autoSave: false);
            var platform = await scope.ServiceProvider.GetRequiredService<
                    IDbContextProvider<SwapMidDbContext>>()
                .GetDbContextAsync();

            options.ConnectionStrings.Default = driftConnectionString;
            options.ConnectionStrings[jobsName] = driftConnectionString;

            await scope.ServiceProvider.GetRequiredService<
                    PipelineFailureNotificationScheduler>()
                .ScheduleAsync(
                    pipelineId,
                    executionId,
                    failure,
                    outcomeUnknown: false);

            var backgroundJobs = (DbContext)await scope.ServiceProvider
                .GetRequiredService<
                    IDbContextProvider<IBackgroundJobsDbContext>>()
                .GetDbContextAsync();
            var platformTransaction = platform.Database.CurrentTransaction ??
                throw new InvalidOperationException(
                    "SwapMidDbContext 未进入真实事务");
            var jobsTransaction = backgroundJobs.Database.CurrentTransaction ??
                throw new InvalidOperationException(
                    "BackgroundJobsDbContext 未进入真实事务");

            ReferenceEquals(
                    platform.Database.GetDbConnection(),
                    backgroundJobs.Database.GetDbConnection())
                .ShouldBeTrue(
                    "执行级 pin 必须屏蔽配置热变并复用原物理连接");
            ReferenceEquals(
                    platformTransaction.GetDbTransaction(),
                    jobsTransaction.GetDbTransaction())
                .ShouldBeTrue(
                    "执行级 pin 必须屏蔽配置热变并复用原物理事务");
            (await ReadTransactionIdAsync(platform))
                .ShouldBe(await ReadTransactionIdAsync(backgroundJobs));

            await unitOfWork.CompleteAsync();
        }
        finally
        {
            options.ConnectionStrings.Default = originalDefault;
            if (hadExplicitJobs)
            {
                options.ConnectionStrings[jobsName] = originalJobs;
            }
            else
            {
                options.ConnectionStrings.Remove(jobsName);
            }
        }

        AssertCommittedFailure(
            await LoadSnapshotAsync(
                originalConnectionString,
                executionId));
        var driftCounts = await LoadExecutionAndJobCountsAsync(
            driftConnectionString,
            executionId);
        driftCounts.ExecutionCount.ShouldBe(0);
        driftCounts.NotificationJobCount.ShouldBe(0);
    }

    private static async Task<string> CreateDriftDatabaseAsync(
        string sourceConnectionString)
    {
        var admin = new NpgsqlConnectionStringBuilder(
            sourceConnectionString)
        {
            Database = "postgres",
        };
        await using (var connection = new NpgsqlConnection(
                         admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE DATABASE \"swapmid_etl_uow_drift\"";
            await command.ExecuteNonQueryAsync();
        }

        var target = new NpgsqlConnectionStringBuilder(
            sourceConnectionString)
        {
            Database = DriftDatabaseName,
        };
        return target.ConnectionString;
    }

    private static async Task<long> ReadTransactionIdAsync(DbContext context)
    {
        await using var command = context.Database.GetDbConnection()
            .CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!
            .GetDbTransaction();
        command.CommandText = "SELECT txid_current()";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task VerifyNormalAtomicCommitAsync(
        IServiceProvider root,
        string connectionString,
        FakeEtlPipelineRunner runner)
    {
        var pipelineId = await CreateActivePipelineAsync(root, "normal");
        var executionId = Guid.NewGuid();
        runner.Reset();
        runner.ExceptionToThrow = new InvalidOperationException(
            "测试故障原文不得进入执行证据或通知参数");

        var execution = await ExecuteAsync(root, pipelineId, executionId);

        execution.Status.ShouldBe(ExecutionStatus.Failed);
        runner.RunCalls.ShouldBe(1);
        AssertCommittedFailure(
            await LoadSnapshotAsync(connectionString, executionId));

        var replay = await ExecuteAsync(root, pipelineId, executionId);
        replay.Status.ShouldBe(ExecutionStatus.Failed);
        runner.RunCalls.ShouldBe(1, "同一 RunId 回放不得再次执行外部 ETL");
        AssertCommittedFailure(
            await LoadSnapshotAsync(connectionString, executionId));
    }

    private static async Task VerifyCommitAcknowledgementLossAsync(
        IServiceProvider root,
        string connectionString,
        FakeEtlPipelineRunner runner)
    {
        var pipelineId = await CreateActivePipelineAsync(root, "ack-loss");
        var executionId = Guid.NewGuid();
        runner.Reset();
        runner.ExceptionToThrow = new InvalidOperationException(
            "测试提交确认丢失");

        var fault = EtlTerminalCommitAckFaultInterceptor.Instance;
        fault.Reset();
        fault.Arm(executionId);

        var execution = await ExecuteAsync(root, pipelineId, executionId);

        execution.Status.ShouldBe(ExecutionStatus.Failed);
        fault.TriggerCount.ShouldBe(1);
        fault.MatchingCommitCount.ShouldBe(
            2,
            "ACK 丢失后必须只重试终态提交一次");
        runner.RunCalls.ShouldBe(1);
        AssertCommittedFailure(
            await LoadSnapshotAsync(connectionString, executionId));

        await ExecuteAsync(root, pipelineId, executionId);
        runner.RunCalls.ShouldBe(1, "提交 ACK 丢失后也不得重放外部 ETL");
        AssertCommittedFailure(
            await LoadSnapshotAsync(connectionString, executionId));
        fault.Reset();
    }

    private static async Task VerifyAtomicRollbackAsync(
        IServiceProvider root,
        string connectionString,
        FakeEtlPipelineRunner runner)
    {
        var pipelineId = await CreateActivePipelineAsync(root, "rollback");
        var executionId = Guid.NewGuid();
        runner.Reset();
        runner.ExceptionToThrow = new InvalidOperationException(
            "测试后台通知入队失败");

        await InstallDeferredCommitFaultAsync(
            connectionString,
            executionId,
            ExecutionStatus.Failed);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            ExecuteAsync(root, pipelineId, executionId));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        exception.ToString().ShouldNotContain(
            "injected ETL terminal/job commit failure");
        runner.RunCalls.ShouldBe(1, "终态提交重试不得重放外部 ETL");
        (await ReadDeferredFaultHitCountAsync(connectionString)).ShouldBe(
            2,
            "协调器的两次终态提交都必须到达延迟触发器，不能由其它故障造成假绿");

        var snapshot = await LoadSnapshotAsync(connectionString, executionId);
        snapshot.Status.ShouldBe(ExecutionStatus.Running);
        snapshot.FinishedAt.ShouldBeNull();
        snapshot.ProcessedRows.ShouldBeNull();
        snapshot.ErrorMessage.ShouldBeNull();
        snapshot.NotificationJobCount.ShouldBe(0);
        using var properties = JsonDocument.Parse(snapshot.ExtraProperties);
        properties.RootElement.TryGetProperty(
                PipelineExecutionFailurePolicy.ErrorCodePropertyName,
                out _)
            .ShouldBeFalse("失败证据必须与通知任务一起回滚");
    }

    private static async Task
        VerifyRecoveryForUpdateSerializesCompetingWritersAsync(
            IServiceProvider root,
            FakeEtlPipelineRunner runner)
    {
        var seed = await CreateRecoverableRunningAsync(
            root,
            "recovery-for-update",
            runner);
        var requestId = Guid.NewGuid();
        var firstLocked = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAttempted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var firstWriter = Task.Run(async () =>
        {
            using var scope = root.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<
                IUnitOfWorkManager>();
            using var unitOfWork = manager.Begin(
                requiresNew: true,
                isTransactional: true);
            var repository = scope.ServiceProvider.GetRequiredService<
                IPipelineExecutionRepository>();
            var execution = await repository.GetForUpdateAsync(
                seed.ExecutionId);
            firstLocked.TrySetResult(true);
            await releaseFirst.Task.WaitAsync(TimeSpan.FromSeconds(10));
            execution.MarkOutcomeUnknown(DateTime.UtcNow);
            execution.SetProperty(
                DataPipelineExecutionCoordinator
                    .RecoveryRequestIdPropertyName,
                requestId.ToString("N"));
            await repository.UpdateAsync(execution, autoSave: false);
            await unitOfWork.CompleteAsync();
        });

        await firstLocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondWriter = Task.Run(async () =>
        {
            using var scope = root.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<
                IUnitOfWorkManager>();
            using var unitOfWork = manager.Begin(
                requiresNew: true,
                isTransactional: true);
            var repository = scope.ServiceProvider.GetRequiredService<
                IPipelineExecutionRepository>();
            secondAttempted.TrySetResult(true);
            var execution = await repository.GetForUpdateAsync(
                seed.ExecutionId);
            await unitOfWork.CompleteAsync();
            return execution;
        });

        try
        {
            await secondAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            secondWriter.IsCompleted.ShouldBeFalse(
                "第二个恢复事务必须阻塞在 SELECT ... FOR UPDATE，不能读取旧 Running 后并发分类");
        }
        finally
        {
            releaseFirst.TrySetResult(true);
        }

        await firstWriter.WaitAsync(TimeSpan.FromSeconds(10));
        var observed = await secondWriter.WaitAsync(TimeSpan.FromSeconds(10));
        observed.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        ReadStringProperty(
                observed,
                DataPipelineExecutionCoordinator
                    .RecoveryRequestIdPropertyName)
            .ShouldBe(requestId.ToString("N"),
                "竞争恢复必须在行锁后看到首个事务提交的 marker");
        runner.RunCalls.ShouldBe(0);
    }

    private static async Task
        VerifyRecoveryRejectsStaleAbpConcurrencyStampAsync(
            IServiceProvider root,
            FakeEtlPipelineRunner runner)
    {
        var seed = await CreateRecoverableRunningAsync(
            root,
            "recovery-concurrency-stamp",
            runner);
        var first = await LoadExecutionEntityAsync(root, seed.ExecutionId);
        var stale = await LoadExecutionEntityAsync(root, seed.ExecutionId);
        first.SetProperty(
            "PipelineExecution.ContractWriter",
            "first");
        await PersistExecutionAsync(root, first);
        stale.SetProperty(
            "PipelineExecution.ContractWriter",
            "stale");

        await Should.ThrowAsync<AbpDbConcurrencyException>(() =>
            PersistExecutionAsync(root, stale));

        var stored = await LoadExecutionEntityAsync(root, seed.ExecutionId);
        stored.ConcurrencyStamp.ShouldNotBe(seed.ConcurrencyStamp,
            "ABP 首次保存必须轮换 ConcurrencyStamp，旧实体测试才有真实前提");
        ReadStringProperty(stored, "PipelineExecution.ContractWriter")
            .ShouldBe("first");
        stored.Status.ShouldBe(ExecutionStatus.Running);
        runner.RunCalls.ShouldBe(0);
    }

    private static async Task
        VerifyCompetingRecoveryRequestsHaveSingleWinnerAsync(
            IServiceProvider root,
            string connectionString,
            FakeEtlPipelineRunner runner)
    {
        var seed = await CreateRecoverableRunningAsync(
            root,
            "recovery-single-winner",
            runner);
        var firstInput = CreateRecoveryInput(seed);
        var secondInput = CreateRecoveryInput(seed);
        var start = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<RecoveryAttempt> AttemptAsync(
            RecoverPipelineExecutionInput input)
        {
            await start.Task;
            try
            {
                var execution = await RecoverRunningAsync(
                    root,
                    seed.ExecutionId,
                    input);
                return new RecoveryAttempt(
                    input.RecoveryRequestId,
                    execution,
                    ErrorCode: null);
            }
            catch (BusinessException exception)
            {
                return new RecoveryAttempt(
                    input.RecoveryRequestId,
                    Execution: null,
                    exception.Code);
            }
        }

        var first = Task.Run(() => AttemptAsync(firstInput));
        var second = Task.Run(() => AttemptAsync(secondInput));
        start.TrySetResult(true);
        var attempts = await Task.WhenAll(first, second)
            .WaitAsync(TimeSpan.FromSeconds(15));

        var winner = attempts
            .Where(attempt => attempt.Execution?.Status ==
                ExecutionStatus.OutcomeUnknown)
            .ShouldHaveSingleItem();
        var loser = attempts
            .Where(attempt => attempt.Execution == null)
            .ShouldHaveSingleItem();
        new[]
        {
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionBusy,
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionRecoveryConflict,
        }.ShouldContain(loser.ErrorCode!);
        winner.RequestId.ShouldNotBe(loser.RequestId);
        runner.RunCalls.ShouldBe(0);

        var snapshot = await LoadSnapshotAsync(
            connectionString,
            seed.ExecutionId);
        snapshot.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        snapshot.NotificationJobCount.ShouldBe(1);
        using var properties = JsonDocument.Parse(snapshot.ExtraProperties);
        properties.RootElement.GetProperty(
                DataPipelineExecutionCoordinator
                    .RecoveryRequestIdPropertyName)
            .GetString()
            .ShouldBe(winner.RequestId.ToString("N"));
    }

    private static async Task VerifyRecoveryCommitAcknowledgementLossAsync(
        IServiceProvider root,
        string connectionString,
        FakeEtlPipelineRunner runner)
    {
        var seed = await CreateRecoverableRunningAsync(
            root,
            "recovery-ack-loss",
            runner);
        var input = CreateRecoveryInput(seed);
        var fault = EtlTerminalCommitAckFaultInterceptor.Instance;
        fault.Reset();
        fault.Arm(seed.ExecutionId, ExecutionStatus.OutcomeUnknown);

        var recovered = await RecoverRunningAsync(
            root,
            seed.ExecutionId,
            input);

        recovered.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        fault.TriggerCount.ShouldBe(1);
        fault.MatchingCommitCount.ShouldBe(
            2,
            "恢复提交 ACK 丢失后必须只重试平台终态事务一次");
        runner.RunCalls.ShouldBe(0);
        var snapshot = await LoadSnapshotAsync(
            connectionString,
            seed.ExecutionId);
        snapshot.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        snapshot.NotificationJobCount.ShouldBe(1,
            "同 RecoveryRequestId 重读必须阻止重复通知任务");
        using (var properties = JsonDocument.Parse(
                   snapshot.ExtraProperties))
        {
            properties.RootElement.GetProperty(
                    DataPipelineExecutionCoordinator
                        .RecoveryRequestIdPropertyName)
                .GetString()
                .ShouldBe(input.RecoveryRequestId.ToString("N"));
        }

        fault.Reset();
    }

    private static async Task VerifyRecoveryFenceLeaseLossRollsBackAsync(
        IServiceProvider root,
        string connectionString,
        FakeEtlPipelineRunner runner)
    {
        var seed = await CreateRecoverableRunningAsync(
            root,
            "recovery-fence-lease-loss",
            runner);
        var fence = root.GetRequiredService<TestDataPipelineExecutionFence>();
        var topology = root.GetRequiredService<
            PostgreSqlRecoveryTopologyBarrier>();
        fence.Reset();
        topology.Reset();
        topology.PauseAt(
            PipelineTerminalStoreTopologyPhase.NotificationSchedule);
        var recoveryTask = Task.Run(() => RecoverRunningAsync(
            root,
            seed.ExecutionId,
            CreateRecoveryInput(seed)));
        BusinessException? observed = null;

        try
        {
            (await Task.Run(() => topology.WaitUntilPaused(
                    TimeSpan.FromSeconds(10))))
                .ShouldBeTrue(
                    "恢复必须在真实 PostgreSQL 事务内到达通知入队 barrier");
            fence.AcquisitionCalls.ShouldBe(1,
                "触发 lease loss 前必须已经取得 PostgreSQL session fence");
            recoveryTask.IsCompleted.ShouldBeFalse();

            fence.LoseCurrentLease();
            topology.ReleasePausedCall();
            observed = await Should.ThrowAsync<BusinessException>(async () =>
            {
                await recoveryTask.WaitAsync(TimeSpan.FromSeconds(15));
            });
        }
        finally
        {
            topology.ReleasePausedCall();
            if (!recoveryTask.IsCompleted)
            {
                try
                {
                    await recoveryTask.WaitAsync(TimeSpan.FromSeconds(15));
                }
                catch
                {
                    // 断言在下方读取持久状态；finally 只负责确保 barrier 不遗留。
                }
            }

            topology.Reset();
            fence.Reset();
        }

        observed.ShouldNotBeNull();
        observed.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        runner.RunCalls.ShouldBe(0);
        var snapshot = await LoadSnapshotAsync(
            connectionString,
            seed.ExecutionId);
        snapshot.Status.ShouldBe(ExecutionStatus.Running);
        snapshot.FinishedAt.ShouldBeNull();
        snapshot.NotificationJobCount.ShouldBe(0,
            "lease loss 必须同时回滚恢复状态、marker 与通知任务");
        using var properties = JsonDocument.Parse(snapshot.ExtraProperties);
        properties.RootElement.TryGetProperty(
                DataPipelineExecutionCoordinator
                    .RecoveryRequestIdPropertyName,
                out _)
            .ShouldBeFalse("失去 fence ownership 后不得留下恢复 marker");
    }

    private static async Task
        VerifyRecoveryCommitAcknowledgementAndOwnershipLossAsync(
            IServiceProvider root,
            string connectionString,
            FakeEtlPipelineRunner runner)
    {
        var seed = await CreateRecoverableRunningAsync(
            root,
            "recovery-ack-and-ownership-loss",
            runner);
        var input = CreateRecoveryInput(seed);
        var fence = root.GetRequiredService<TestDataPipelineExecutionFence>();
        var fault = EtlTerminalCommitAckFaultInterceptor.Instance;
        fence.Reset();
        fault.Reset();
        fault.Arm(
            seed.ExecutionId,
            ExecutionStatus.OutcomeUnknown,
            fence.LoseCurrentLease);

        try
        {
            var exception = await Should.ThrowAsync<BusinessException>(() =>
                RecoverRunningAsync(
                    root,
                    seed.ExecutionId,
                    input));

            exception.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionCoordinationUnavailable);
            fault.TriggerCount.ShouldBe(1);
            fault.MatchingCommitCount.ShouldBe(1,
                "ACK 丢失同时失去 ownership 后不得启动第二笔无锁确认事务");
            runner.RunCalls.ShouldBe(0);
            var snapshot = await LoadSnapshotAsync(
                connectionString,
                seed.ExecutionId);
            snapshot.Status.ShouldBe(ExecutionStatus.OutcomeUnknown,
                "首次事务已经提交，调用失败也不得伪装成仍可重跑的 Running");
            snapshot.NotificationJobCount.ShouldBe(1);
            using var properties = JsonDocument.Parse(
                snapshot.ExtraProperties);
            properties.RootElement.GetProperty(
                    DataPipelineExecutionCoordinator
                        .RecoveryRequestIdPropertyName)
                .GetString()
                .ShouldBe(input.RecoveryRequestId.ToString("N"));
        }
        finally
        {
            fault.Reset();
            fence.Reset();
        }
    }

    private static async Task VerifyRecoveryDeferredJobFaultRollsBackAsync(
        IServiceProvider root,
        string connectionString,
        FakeEtlPipelineRunner runner)
    {
        var seed = await CreateRecoverableRunningAsync(
            root,
            "recovery-deferred-job-fault",
            runner);
        var hitCountBefore = await ReadDeferredFaultHitCountAsync(
            connectionString);
        await InstallDeferredCommitFaultAsync(
            connectionString,
            seed.ExecutionId,
            ExecutionStatus.OutcomeUnknown);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            RecoverRunningAsync(
                root,
                seed.ExecutionId,
                CreateRecoveryInput(seed)));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        runner.RunCalls.ShouldBe(0);
        (await ReadDeferredFaultHitCountAsync(connectionString) -
         hitCountBefore).ShouldBe(
            2,
            "恢复终态的两次提交都必须到达延迟触发器");
        var snapshot = await LoadSnapshotAsync(
            connectionString,
            seed.ExecutionId);
        snapshot.Status.ShouldBe(ExecutionStatus.Running);
        snapshot.FinishedAt.ShouldBeNull();
        snapshot.NotificationJobCount.ShouldBe(0);
        using var properties = JsonDocument.Parse(snapshot.ExtraProperties);
        properties.RootElement.TryGetProperty(
                DataPipelineExecutionCoordinator
                    .RecoveryRequestIdPropertyName,
                out _)
            .ShouldBeFalse("恢复 marker 必须与通知任务一起回滚");
        properties.RootElement.TryGetProperty(
                PipelineExecutionFailurePolicy.ErrorCodePropertyName,
                out _)
            .ShouldBeFalse("OutcomeUnknown 证据必须与通知任务一起回滚");
    }

    private static void AssertCommittedFailure(ExecutionSnapshot snapshot)
    {
        snapshot.Status.ShouldBe(ExecutionStatus.Failed);
        snapshot.FinishedAt.ShouldNotBeNull();
        snapshot.ProcessedRows.ShouldBeNull();
        snapshot.ErrorMessage.ShouldBeNull();
        snapshot.NotificationJobCount.ShouldBe(1);

        using var properties = JsonDocument.Parse(snapshot.ExtraProperties);
        properties.RootElement.TryGetProperty(
                PipelineExecutionFailurePolicy.ErrorCodePropertyName,
                out var errorCode)
            .ShouldBeTrue();
        errorCode.GetString().ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionFailed);
    }

    private static async Task CreateRequiredSchemasAsync(string connectionString)
    {
        await using var host = new SwapMidDbContext(
            new DbContextOptionsBuilder<SwapMidDbContext>()
                .UseNpgsql(connectionString)
                .Options);
        await host.GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync();
    }

    private static async Task<RecoverySeed> CreateRecoverableRunningAsync(
        IServiceProvider root,
        string scenario,
        FakeEtlPipelineRunner runner)
    {
        var pipelineId = await CreateActivePipelineAsync(root, scenario);
        var completedId = Guid.NewGuid();
        runner.Reset();
        var completed = await ExecuteAsync(root, pipelineId, completedId);
        completed.Status.ShouldBe(ExecutionStatus.Succeeded);
        var completedEntity = await LoadExecutionEntityAsync(
            root,
            completedId);
        var running = new PipelineExecution(
            Guid.NewGuid(),
            pipelineId,
            $"pg-{scenario}-recoverable",
            TriggerType.Manual,
            completedEntity.TargetResourceHash,
            completedEntity.VerifiedTargetResourceHash,
            completedEntity.EvidenceProtocol,
            completedEntity.TargetResourceInstanceId,
            completedEntity.ExecutionContractHash,
            completedEntity.TargetCoordinationIdentityHash);
        typeof(PipelineExecution)
            .GetProperty(
                nameof(PipelineExecution.StartedAt),
                BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(running, DateTime.UtcNow.AddMinutes(-10));
        foreach (var property in completedEntity.ExtraProperties)
        {
            running.SetProperty(property.Key, property.Value);
        }

        await WithUnitOfWorkAsync(root, async services =>
        {
            await services.GetRequiredService<
                    IPipelineExecutionRepository>()
                .InsertAsync(running, autoSave: false);
            return true;
        });
        var stored = await LoadExecutionEntityAsync(root, running.Id);
        runner.Reset();
        return new RecoverySeed(
            pipelineId,
            stored.Id,
            stored.ConcurrencyStamp);
    }

    private static RecoverPipelineExecutionInput CreateRecoveryInput(
        RecoverySeed seed)
    {
        var now = DateTimeOffset.UtcNow;
        return new RecoverPipelineExecutionInput
        {
            RecoveryRequestId = Guid.NewGuid(),
            ExpectedConcurrencyStamp = seed.ConcurrencyStamp,
            DrainStartedAt = now.AddMinutes(-8),
            OldOwnerExitVerifiedAt = now.AddMinutes(-7),
            TargetSessionExitVerifiedAt = now.AddMinutes(-6),
            EvidenceReference =
                "incident:INC-2026-PG-RECOVERY-CONTRACT",
        };
    }

    private static async Task<PipelineExecution> RecoverRunningAsync(
        IServiceProvider root,
        Guid executionId,
        RecoverPipelineExecutionInput input)
    {
        using var scope = root.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<DataPipelineExecutionCoordinator>()
            .RecoverRunningAsync(
                executionId,
                input,
                Guid.NewGuid());
    }

    private static Task<PipelineExecution> LoadExecutionEntityAsync(
        IServiceProvider root,
        Guid executionId)
        => WithUnitOfWorkAsync(root, services =>
            services.GetRequiredService<IPipelineExecutionRepository>()
                .GetAsync(executionId, includeDetails: false));

    private static async Task PersistExecutionAsync(
        IServiceProvider root,
        PipelineExecution execution)
    {
        using var scope = root.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<
            IUnitOfWorkManager>();
        using var unitOfWork = manager.Begin(
            requiresNew: true,
            isTransactional: true);
        await scope.ServiceProvider.GetRequiredService<
                IPipelineExecutionRepository>()
            .UpdateAsync(execution, autoSave: false);
        await unitOfWork.CompleteAsync();
    }

    private static string? ReadStringProperty(
        PipelineExecution execution,
        string propertyName)
    {
        if (!execution.ExtraProperties.TryGetValue(
                propertyName,
                out var value))
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

    private static Task<Guid> CreateActivePipelineAsync(
        IServiceProvider root,
        string scenario)
        => WithUnitOfWorkAsync(root, async services =>
        {
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var source = new DataSource(
                Guid.NewGuid(),
                $"pg-{scenario}-source-{suffix}",
                DataSourceType.RelationalDatabase,
                CreateDataSourceConfig(
                    "source.test",
                    $"test:etl-source-{suffix}"));
            var target = new DataSource(
                Guid.NewGuid(),
                $"pg-{scenario}-target-{suffix}",
                DataSourceType.RelationalDatabase,
                CreateDataSourceConfig(
                    "target.test",
                    $"test:etl-target-{suffix}"));
            var pipeline = new DataPipeline(
                Guid.NewGuid(),
                $"pg-{scenario}-pipeline-{suffix}",
                source.Id,
                target.Id,
                transformConfig: JsonSerializer.Serialize(new
                {
                    sourceQuery = "SELECT 1 AS Id",
                    targetTable = $"dbo.Target{suffix}",
                    mode = "append",
                }));
            pipeline.Activate();

            var sourceRepository = services.GetRequiredService<
                IRepository<DataSource, Guid>>();
            await sourceRepository.InsertAsync(source, autoSave: false);
            await sourceRepository.InsertAsync(target, autoSave: false);
            await services.GetRequiredService<IDataPipelineRepository>()
                .InsertAsync(pipeline, autoSave: false);
            return pipeline.Id;
        });

    private static string CreateDataSourceConfig(
        string host,
        string targetResourceGroup)
        => JsonSerializer.Serialize(new
        {
            provider = "sqlserver",
            host,
            port = 1433,
            database = "Han",
            username = "writer",
            targetResourceGroup,
        });

    private static async Task<PipelineExecution> ExecuteAsync(
        IServiceProvider root,
        Guid pipelineId,
        Guid executionId)
    {
        using var scope = root.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<DataPipelineExecutionCoordinator>()
            .ExecuteAsync(
                pipelineId,
                executionId,
                TriggerType.Manual);
    }

    private static async Task<TResult> WithUnitOfWorkAsync<TResult>(
        IServiceProvider root,
        Func<IServiceProvider, Task<TResult>> action)
    {
        using var scope = root.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<
            IUnitOfWorkManager>();
        using var unitOfWork = manager.Begin(
            requiresNew: true,
            isTransactional: true);
        var result = await action(scope.ServiceProvider);
        await unitOfWork.CompleteAsync();
        return result;
    }

    private static async Task<ExecutionSnapshot> LoadSnapshotAsync(
        string connectionString,
        Guid executionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var executionCommand = connection.CreateCommand();
        executionCommand.CommandText =
            """
            SELECT "Status", "FinishedAt", "ProcessedRows", "ErrorMessage", "ExtraProperties"
            FROM "AppPipelineExecutions"
            WHERE "Id" = @executionId
            """;
        executionCommand.Parameters.AddWithValue("executionId", executionId);

        ExecutionStatus status;
        DateTime? finishedAt;
        long? processedRows;
        string? errorMessage;
        string extraProperties;
        await using (var reader = await executionCommand.ExecuteReaderAsync())
        {
            (await reader.ReadAsync()).ShouldBeTrue();
            status = (ExecutionStatus)reader.GetInt32(0);
            finishedAt = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
            processedRows = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            errorMessage = reader.IsDBNull(3) ? null : reader.GetString(3);
            extraProperties = reader.GetString(4);
        }

        var notificationJobCount = 0;
        await using var jobCommand = connection.CreateCommand();
        jobCommand.CommandText =
            """
            SELECT "JobArgs"
            FROM "AbpBackgroundJobs"
            WHERE "JobName" = @jobName
            """;
        jobCommand.Parameters.AddWithValue(
            "jobName",
            BackgroundJobNameAttribute.GetName<
                PipelineFailureNotificationJobArgs>());
        await using (var reader = await jobCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var args = JsonSerializer.Deserialize<
                    PipelineFailureNotificationJobArgs>(
                    reader.GetString(0),
                    JobArgsJsonOptions);
                if (args?.ExecutionId == executionId)
                {
                    notificationJobCount++;
                }
            }
        }

        return new ExecutionSnapshot(
            status,
            finishedAt,
            processedRows,
            errorMessage,
            extraProperties,
            notificationJobCount);
    }

    private static async Task<ExecutionAndJobCounts>
        LoadExecutionAndJobCountsAsync(
            string connectionString,
            Guid executionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*)
                 FROM "AppPipelineExecutions"
                 WHERE "Id" = @executionId),
                (SELECT COUNT(*)
                 FROM "AbpBackgroundJobs"
                 WHERE "JobName" = @jobName
                   AND COALESCE(
                       "JobArgs"::jsonb ->> 'ExecutionId',
                       "JobArgs"::jsonb ->> 'executionId') = @executionIdText)
            """;
        command.Parameters.AddWithValue("executionId", executionId);
        command.Parameters.AddWithValue(
            "executionIdText",
            executionId.ToString("D"));
        command.Parameters.AddWithValue(
            "jobName",
            BackgroundJobNameAttribute.GetName<
                PipelineFailureNotificationJobArgs>());
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        return new ExecutionAndJobCounts(
            reader.GetInt64(0),
            reader.GetInt64(1));
    }

    private static async Task InstallDeferredCommitFaultAsync(
        string connectionString,
        Guid executionId,
        ExecutionStatus expectedStatus)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var schemaCommand = connection.CreateCommand())
        {
            // 延迟约束触发器在 COMMIT 时才执行。它只有同时看到目标执行已变为 Failed、
            // 且对应通知行也在当前事务中时才抛错，因此能区分“真正回滚”与“更新尚未执行”。
            schemaCommand.CommandText =
                """
                CREATE TABLE IF NOT EXISTS "EtlTerminalJobFaultControl" (
                    "ExecutionId" uuid PRIMARY KEY,
                    "JobName" text NOT NULL,
                    "ExpectedStatus" integer NOT NULL
                );

                CREATE SEQUENCE IF NOT EXISTS "EtlTerminalJobFaultHits";

                CREATE OR REPLACE FUNCTION "RaiseEtlTerminalJobAtomicityFault"()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $fault$
                DECLARE
                    execution_id_text text;
                BEGIN
                    execution_id_text := COALESCE(
                        NEW."JobArgs"::jsonb ->> 'ExecutionId',
                        NEW."JobArgs"::jsonb ->> 'executionId');
                    IF execution_id_text IS NOT NULL AND EXISTS (
                        SELECT 1
                        FROM "AppPipelineExecutions" AS e
                        INNER JOIN "EtlTerminalJobFaultControl" AS c
                            ON c."ExecutionId" = e."Id"
                        WHERE e."Id"::text = lower(execution_id_text)
                          AND e."Status" = c."ExpectedStatus"
                          AND c."JobName" = NEW."JobName"
                    ) THEN
                        PERFORM nextval('"EtlTerminalJobFaultHits"');
                        RAISE EXCEPTION 'injected ETL terminal/job commit failure'
                            USING ERRCODE = 'P0001';
                    END IF;
                    RETURN NEW;
                END;
                $fault$;

                DROP TRIGGER IF EXISTS "TR_EtlTerminalJobAtomicityFault"
                    ON "AbpBackgroundJobs";
                CREATE CONSTRAINT TRIGGER "TR_EtlTerminalJobAtomicityFault"
                AFTER INSERT ON "AbpBackgroundJobs"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION "RaiseEtlTerminalJobAtomicityFault"();
                """;
            await schemaCommand.ExecuteNonQueryAsync();
        }

        await using var controlCommand = connection.CreateCommand();
        controlCommand.CommandText =
            """
            INSERT INTO "EtlTerminalJobFaultControl"
                ("ExecutionId", "JobName", "ExpectedStatus")
            VALUES (@executionId, @jobName, @expectedStatus)
            """;
        controlCommand.Parameters.AddWithValue("executionId", executionId);
        controlCommand.Parameters.AddWithValue(
            "jobName",
            BackgroundJobNameAttribute.GetName<
                PipelineFailureNotificationJobArgs>());
        controlCommand.Parameters.AddWithValue(
            "expectedStatus",
            (int)expectedStatus);
        await controlCommand.ExecuteNonQueryAsync();
    }

    private static async Task<long> ReadDeferredFaultHitCountAsync(
        string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT last_value FROM \"EtlTerminalJobFaultHits\"";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed record ExecutionSnapshot(
        ExecutionStatus Status,
        DateTime? FinishedAt,
        long? ProcessedRows,
        string? ErrorMessage,
        string ExtraProperties,
        int NotificationJobCount);

    private sealed record ExecutionAndJobCounts(
        long ExecutionCount,
        long NotificationJobCount);

    private sealed record RecoverySeed(
        Guid PipelineId,
        Guid ExecutionId,
        string ConcurrencyStamp);

    private sealed record RecoveryAttempt(
        Guid RequestId,
        PipelineExecution? Execution,
        string? ErrorCode);
}

internal sealed class PostgreSqlRecoveryTopologyBarrier
{
    private PausePoint? _pausePoint;

    public void PauseAt(PipelineTerminalStoreTopologyPhase phase)
    {
        var pausePoint = new PausePoint(phase);
        if (Interlocked.CompareExchange(
                ref _pausePoint,
                pausePoint,
                null) != null)
        {
            throw new InvalidOperationException("PostgreSQL 恢复 barrier 已存在");
        }
    }

    public void Reach(PipelineTerminalStoreTopologyPhase phase)
    {
        var pausePoint = Volatile.Read(ref _pausePoint);
        if (pausePoint?.Phase != phase)
        {
            return;
        }

        pausePoint.Entered.Set();
        if (!pausePoint.Release.Wait(TimeSpan.FromSeconds(15)))
        {
            throw new TimeoutException(
                $"PostgreSQL 恢复 barrier 等待释放超时：{phase}");
        }
    }

    public bool WaitUntilPaused(TimeSpan timeout)
        => Volatile.Read(ref _pausePoint)?.Entered.Wait(timeout) == true;

    public void ReleasePausedCall()
        => Volatile.Read(ref _pausePoint)?.Release.Set();

    public void Reset()
        => Interlocked.Exchange(ref _pausePoint, null)?.Release.Set();

    private sealed class PausePoint(
        PipelineTerminalStoreTopologyPhase phase)
    {
        public PipelineTerminalStoreTopologyPhase Phase { get; } = phase;
        public ManualResetEventSlim Entered { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
    }
}

internal sealed class PostgreSqlPipelineTerminalStoreTopologyGuard(
    PipelineTerminalStoreTopologyGuard inner,
    PostgreSqlRecoveryTopologyBarrier barrier)
    : IPipelineTerminalStoreTopologyGuard
{
    public Task<IPipelineTerminalStoreTopologyPin> CaptureSafeAsync(
        PipelineTerminalStoreTopologyPhase phase,
        CancellationToken cancellationToken = default)
        => inner.CaptureSafeAsync(phase, cancellationToken);

    public IDisposable EnterSafeScope(
        IPipelineTerminalStoreTopologyPin pin,
        PipelineTerminalStoreTopologyPhase phase)
        => inner.EnterSafeScope(pin, phase);

    public void EnsureSafeScope(PipelineTerminalStoreTopologyPhase phase)
    {
        inner.EnsureSafeScope(phase);
        barrier.Reach(phase);
    }
}

/// <summary>模拟数据库已提交、客户端未收到 ACK 的单次故障。</summary>
public sealed class EtlTerminalCommitAckFaultInterceptor : DbTransactionInterceptor
{
    private int _armed;
    private int _matchingCommitCount;
    private int _triggerCount;
    private int _expectedStatus = (int)ExecutionStatus.Failed;
    private string? _executionId;
    private Action? _afterCommitBeforeAckFailure;

    public static EtlTerminalCommitAckFaultInterceptor Instance { get; } = new();

    public int TriggerCount => Volatile.Read(ref _triggerCount);

    public int MatchingCommitCount => Volatile.Read(
        ref _matchingCommitCount);

    public void Arm(
        Guid executionId,
        ExecutionStatus expectedStatus = ExecutionStatus.Failed,
        Action? afterCommitBeforeAckFailure = null)
    {
        Volatile.Write(ref _executionId, executionId.ToString("D"));
        Volatile.Write(ref _expectedStatus, (int)expectedStatus);
        Volatile.Write(ref _matchingCommitCount, 0);
        Volatile.Write(ref _triggerCount, 0);
        Volatile.Write(
            ref _afterCommitBeforeAckFailure,
            afterCommitBeforeAckFailure);
        Volatile.Write(ref _armed, 1);
    }

    public void Reset()
    {
        Volatile.Write(ref _armed, 0);
        Volatile.Write(ref _matchingCommitCount, 0);
        Volatile.Write(ref _triggerCount, 0);
        Volatile.Write(ref _expectedStatus, (int)ExecutionStatus.Failed);
        Volatile.Write(ref _executionId, null);
        Volatile.Write(ref _afterCommitBeforeAckFailure, null);
    }

    public override void TransactionCommitted(
        DbTransaction transaction,
        TransactionEndEventData eventData)
        => ThrowIfArmed(eventData);

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ThrowIfArmed(eventData);
        return Task.CompletedTask;
    }

    private void ThrowIfArmed(TransactionEndEventData eventData)
    {
        var expectedId = Volatile.Read(ref _executionId);
        if (!Guid.TryParse(expectedId, out var executionId) ||
            eventData.Context is not SwapMidDbContext context ||
            !context.ChangeTracker.Entries<PipelineExecution>().Any(entry =>
                entry.Entity.Id == executionId &&
                entry.Entity.Status ==
                (ExecutionStatus)Volatile.Read(ref _expectedStatus)))
        {
            return;
        }

        Interlocked.Increment(ref _matchingCommitCount);
        if (Volatile.Read(ref _armed) != 1 ||
            Interlocked.Exchange(ref _armed, 0) != 1)
        {
            return;
        }

        Interlocked.Increment(ref _triggerCount);
        Interlocked.Exchange(
                ref _afterCommitBeforeAckFailure,
                null)
            ?.Invoke();
        throw new InvalidOperationException(
            "测试注入：ETL 终态事务已提交但确认响应失败");
    }
}

[DependsOn(
    typeof(SwapMidApplicationTestModule),
    typeof(SwapMidEntityFrameworkCoreModule))]
public sealed class DataPipelineExecutionPostgreSqlContractTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<FeatureManagementOptions>(options =>
        {
            options.SaveStaticFeaturesToDatabase = false;
            options.IsDynamicFeatureStoreEnabled = false;
        });
        Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = false;
        });
        Configure<SettingManagementOptions>(options =>
        {
            options.SaveStaticSettingsToDatabase = false;
            options.IsDynamicSettingStoreEnabled = false;
        });
        Configure<AbpBackgroundJobOptions>(options =>
        {
            options.IsJobExecutionEnabled = false;
        });
        Configure<AbpDbContextOptions>(options =>
        {
            options.PreConfigure<SwapMidDbContext>(dbContext =>
                dbContext.DbContextOptions.AddInterceptors(
                    EtlTerminalCommitAckFaultInterceptor.Instance));
        });

        context.Services.AddSingleton<TestDataPipelineExecutionFence>();
        context.Services.Replace(ServiceDescriptor.Singleton<
            IDataPipelineExecutionFence>(provider =>
                provider.GetRequiredService<TestDataPipelineExecutionFence>()));
        context.Services.AddSingleton<PostgreSqlRecoveryTopologyBarrier>();
        context.Services.Replace(ServiceDescriptor.Transient<
            IPipelineTerminalStoreTopologyGuard,
            PostgreSqlPipelineTerminalStoreTopologyGuard>());
        context.Services.Replace(ServiceDescriptor.Singleton<
            IEtlPipelineRunner,
            FakeEtlPipelineRunner>());
        context.Services.Replace(ServiceDescriptor.Transient<
            IDataSeeder,
            DataPipelineExecutionNoOpDataSeeder>());
    }
}

public sealed class DataPipelineExecutionNoOpDataSeeder : IDataSeeder
{
    public Task SeedAsync(DataSeedContext context) => Task.CompletedTask;
}

[CollectionDefinition(
    DataPipelineExecutionPostgreSqlContractCollection.Name,
    DisableParallelization = true)]
public sealed class DataPipelineExecutionPostgreSqlContractCollection
{
    public const string Name = "PostgreSQL:DataIntegration.ExecutionUow";
}
