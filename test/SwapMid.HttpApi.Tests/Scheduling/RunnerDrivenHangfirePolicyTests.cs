using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.EnterpriseManagement;
using SwapMid.Hangfire;
using SwapMid.HttpApiHost.Scheduling;
using SwapMid.Notification;
using SwapMid.Scheduling;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Volo.Abp.BackgroundJobs.Hangfire;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.HttpApi.Tests.Scheduling;

public class RunnerDrivenHangfirePolicyTests
{
    [Fact]
    public void Catalog全部入口必须由Runner驱动并应用方法级零重试()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var provider = new RunnerDrivenJobRetryFilterProvider();

        // 4 个平台通用后台清理任务。
        ScheduledJobCatalog.All.Count.ShouldBe(4);
        foreach (var definition in ScheduledJobCatalog.All)
        {
            definition.Register(manager, "* * * * *");
        }

        var registrations = manager.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Select(call => call.GetArguments())
            .ToList();

        registrations.Count.ShouldBe(ScheduledJobCatalog.All.Count);
        foreach (var arguments in registrations)
        {
            var job = arguments.OfType<Job>().Single();
            RunnerDrivenHangfireJobClassifier.IsRunnerDriven(job).ShouldBeTrue();
            job.Method.GetCustomAttribute<AsyncStateMachineAttribute>().ShouldBeNull(
                "生产入口不得用 async/await 包装 Runner 的 faulted typed OCE Task");
            job.Method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ShouldBe(new[] { typeof(CancellationToken) });
            job.Args.Single()
                .ShouldBeOfType<CancellationToken>()
                .ShouldBe(CancellationToken.None);

            var retry = provider.GetFilters(job).Single();
            retry.Scope.ShouldBe(JobFilterScope.Method);
            var automaticRetry = retry.Instance.ShouldBeOfType<AutomaticRetryAttribute>();
            automaticRetry.Attempts.ShouldBe(0);
        }
    }

    [Fact]
    public void 方法级零重试在Provider聚合后必须覆盖默认全局策略()
    {
        var globals = new JobFilterCollection();
        globals.Add(new AutomaticRetryAttribute { Attempts = 10 });
        var providers = new JobFilterProviderCollection(
            globals,
            new RunnerDrivenJobRetryFilterProvider());

        var retries = providers
            .GetFilters(CreateJob(typeof(DirectRunnerJob)))
            .Where(filter => filter.Instance is AutomaticRetryAttribute)
            .ToList();

        retries.Count.ShouldBe(1);
        retries.Single().Scope.ShouldBe(JobFilterScope.Method);
        ((AutomaticRetryAttribute)retries.Single().Instance).Attempts.ShouldBe(0);
    }

    [Fact]
    public void AbpAdapter只识别带Runner参数Marker的精确闭包()
    {
        var adapterDefinition = GetAdapterDefinition();

        var runnerAdapter = adapterDefinition.MakeGenericType(typeof(RunScheduledJobArgs));
        var unrelatedAdapter = adapterDefinition.MakeGenericType(typeof(UnrelatedArgs));

        RunnerDrivenHangfireJobClassifier.IsRunnerDrivenType(runnerAdapter).ShouldBeTrue();
        RunnerDrivenHangfireJobClassifier.IsRunnerDriven(CreateAdapterJob(runnerAdapter)).ShouldBeTrue();
        RunnerDrivenHangfireJobClassifier.IsRunnerDrivenType(unrelatedAdapter).ShouldBeFalse();
        RunnerDrivenHangfireJobClassifier.IsRunnerDrivenType(typeof(UnrelatedJob)).ShouldBeFalse();

        var wrongMethod = typeof(DirectRunnerJob).GetMethod(nameof(DirectRunnerJob.OtherAsync))!;
        RunnerDrivenHangfireJobClassifier.IsRunnerDriven(
            new Job(
                typeof(DirectRunnerJob),
                wrongMethod,
            new object[] { CancellationToken.None })).ShouldBeFalse();
    }

    [Fact]
    public void 手工直接入口必须被精确分类且没有Async状态机()
    {
        var method = typeof(RunScheduledJobHangfireJob).GetMethod(
            nameof(RunScheduledJobHangfireJob.ExecuteAsync),
            [typeof(RunScheduledJobArgs), typeof(CancellationToken)])
            ?? throw new InvalidOperationException("缺少手工直接 Hangfire 入口");
        var job = new Job(
            typeof(RunScheduledJobHangfireJob),
            method,
            new object[]
            {
                new RunScheduledJobArgs { JobKey = "manual-job" },
                CancellationToken.None,
            });

        RunnerDrivenHangfireJobClassifier.IsRunnerDriven(job).ShouldBeTrue();
        method.GetCustomAttribute<AsyncStateMachineAttribute>().ShouldBeNull();
        new RunnerDrivenJobRetryFilterProvider()
            .GetFilters(job)
            .Single()
            .Instance
            .ShouldBeOfType<AutomaticRetryAttribute>()
            .Attempts
            .ShouldBe(0);
    }

    [Fact]
    public void Abp手工后台入口在进入Runner前失败仍必须按参数任务Key归位()
    {
        const string jobKey = "stable-manual-job";
        var executionId = Guid.NewGuid();
        var adapterType = GetAdapterDefinition()
            .MakeGenericType(typeof(RunScheduledJobArgs));
        var recorder = Substitute.For<IScheduledJobExecutionRecorder>();
        recorder
            .StartAsync(
                jobKey,
                adapterType.Name,
                "ExecuteAsync",
                "job-fallback",
                JobTriggerKind.Manual,
                false,
                null)
            .Returns(new ScheduledJobExecutionStartResult(executionId, Recorded: true));
        recorder
            .FinishAsync(
                executionId,
                false,
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(true);
        using var services = new ServiceCollection()
            .AddSingleton(recorder)
            .BuildServiceProvider();
        var filter = new ScheduledJobExecutionFilter(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>());
        var job = CreateAdapterJob(
            adapterType,
            new RunScheduledJobArgs { JobKey = jobKey });

        filter.OnPerformed(CreatePerformedContext(
            job,
            new InvalidOperationException("adapter activation failed")));

        Received.InOrder(() =>
        {
            recorder.StartAsync(
                jobKey,
                adapterType.Name,
                "ExecuteAsync",
                "job-fallback",
                JobTriggerKind.Manual,
                false,
                null);
            recorder.FinishAsync(
                executionId,
                false,
                Arg.Any<string?>(),
                Arg.Any<string?>());
        });
    }

    [Fact]
    public void Recurring任务Key必须优先于参数Key()
    {
        const string recurringJobKey = "catalog-job-key";
        const string payloadJobKey = "stale-payload-job-key";
        var executionId = Guid.NewGuid();
        var adapterType = GetAdapterDefinition()
            .MakeGenericType(typeof(RunScheduledJobArgs));
        var recorder = Substitute.For<IScheduledJobExecutionRecorder>();
        recorder
            .StartAsync(
                recurringJobKey,
                adapterType.Name,
                "ExecuteAsync",
                "job-fallback",
                JobTriggerKind.Scheduled,
                false,
                null)
            .Returns(new ScheduledJobExecutionStartResult(executionId, Recorded: true));
        recorder
            .FinishAsync(
                executionId,
                false,
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(true);
        using var services = new ServiceCollection()
            .AddSingleton(recorder)
            .BuildServiceProvider();
        var filter = new ScheduledJobExecutionFilter(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>());
        var job = CreateAdapterJob(
            adapterType,
            new RunScheduledJobArgs { JobKey = payloadJobKey });

        filter.OnPerformed(CreatePerformedContext(
            job,
            new InvalidOperationException("adapter activation failed"),
            recurringJobKey));

        recorder.Received(1).StartAsync(
            recurringJobKey,
            adapterType.Name,
            "ExecuteAsync",
            "job-fallback",
            JobTriggerKind.Scheduled,
            false,
            null);
        recorder.DidNotReceive().StartAsync(
            payloadJobKey,
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<JobTriggerKind>(),
            Arg.Any<bool>(),
            Arg.Any<string?>());
    }

    [Fact]
    public void Abp手工后台入口原始取消也必须按参数任务Key补Interrupted()
    {
        const string jobKey = "stable-cancelled-manual-job";
        var executionId = Guid.NewGuid();
        var adapterType = GetAdapterDefinition()
            .MakeGenericType(typeof(RunScheduledJobArgs));
        var recorder = Substitute.For<IScheduledJobExecutionRecorder>();
        recorder
            .StartAsync(
                jobKey,
                adapterType.Name,
                "ExecuteAsync",
                "job-fallback",
                JobTriggerKind.Manual,
                false,
                null)
            .Returns(new ScheduledJobExecutionStartResult(executionId, Recorded: true));
        recorder
            .InterruptAsync(executionId, Arg.Any<string?>())
            .Returns(true);
        using var services = new ServiceCollection()
            .AddSingleton(recorder)
            .BuildServiceProvider();
        var filter = new ScheduledJobExecutionFilter(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>());
        var job = CreateAdapterJob(
            adapterType,
            new RunScheduledJobArgs { JobKey = jobKey });

        filter.OnPerformed(CreatePerformedContext(
            job,
            new OperationCanceledException("adapter cancelled")));

        Received.InOrder(() =>
        {
            recorder.StartAsync(
                jobKey,
                adapterType.Name,
                "ExecuteAsync",
                "job-fallback",
                JobTriggerKind.Manual,
                false,
                null);
            recorder.InterruptAsync(
                executionId,
                Arg.Any<string?>());
        });
    }

    [Fact]
    public void 仅当全部异常叶均确认记录时才允许抑制补偿()
    {
        var executionId = Guid.NewGuid();
        var recorded = new ScheduledJobRecordedFailureException(
            "job-key",
            executionId,
            terminalStateRecorded: true,
            failureNotificationHandled: true);
        var wrapped = new TargetInvocationException(new Exception("middle", recorded));
        var mixed = new AggregateException(
            recorded,
            new InvalidOperationException("other"));

        ScheduledJobRecordedFailureInspector.AllFailuresAreRecorded(wrapped).ShouldBeTrue();
        ScheduledJobRecordedFailureInspector
            .AllFailureNotificationsAreHandled(wrapped)
            .ShouldBeTrue();
        ScheduledJobRecordedFailureInspector.AllFailuresAreRecorded(mixed).ShouldBeFalse();
        var unconfirmed = new ScheduledJobRecordedFailureException(
            "job-key",
            executionId,
            terminalStateRecorded: false,
            failureNotificationHandled: false);
        ScheduledJobRecordedFailureInspector.AllFailuresAreRecorded(unconfirmed).ShouldBeFalse();
        ScheduledJobRecordedFailureInspector
            .AllFailureNotificationsAreHandled(unconfirmed)
            .ShouldBeFalse();
        var notificationFailed = new ScheduledJobRecordedFailureException(
            "job-key",
            executionId,
            terminalStateRecorded: true,
            failureNotificationHandled: false);
        ScheduledJobRecordedFailureInspector
            .AllFailuresAreRecorded(notificationFailed)
            .ShouldBeTrue();
        ScheduledJobRecordedFailureInspector
            .AllFailureNotificationsAreHandled(notificationFailed)
            .ShouldBeFalse();

        var recordedCancellation = new ScheduledJobRecordedCancellationException(
            "job-key",
            executionId,
            terminalStateRecorded: true,
            cancellationToken: new CancellationToken(canceled: true));
        ScheduledJobRecordedFailureInspector
            .AllLeavesAreCancellation(new TargetInvocationException(recordedCancellation))
            .ShouldBeTrue();
        ScheduledJobRecordedFailureInspector
            .AllCancellationsAreRecorded(recordedCancellation)
            .ShouldBeTrue();
        ScheduledJobRecordedFailureInspector
            .AllLeavesAreCancellation(new AggregateException(
                recordedCancellation,
                new InvalidOperationException("other")))
            .ShouldBeFalse();
        recordedCancellation.InnerException.ShouldBeNull();

        var unconfirmedSkipped = new ScheduledJobUnconfirmedSkippedException(
            "job-key",
            executionId);
        ScheduledJobRecordedFailureInspector
            .AllLeavesAreUnconfirmedSkippedMarkers(
                new TargetInvocationException(unconfirmedSkipped))
            .ShouldBeTrue();
        ScheduledJobRecordedFailureInspector
            .AllLeavesAreUnconfirmedSkippedMarkers(new AggregateException(
                unconfirmedSkipped,
                new InvalidOperationException("other")))
            .ShouldBeFalse();
    }

    [Fact]
    public void ExecutionFilter遇到已记录失败不得补第二条历史()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var filter = new ScheduledJobExecutionFilter(
            scopeFactory,
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>());
        var job = CreateJob(typeof(DirectRunnerJob));
        var backgroundJob = new BackgroundJob("job-1", job, DateTime.UtcNow);
        var performContext = new PerformContext(
            Substitute.For<JobStorage>(),
            Substitute.For<IStorageConnection>(),
            backgroundJob,
            Substitute.For<IJobCancellationToken>());
        var context = new PerformedContext(
            performContext,
            result: null,
            canceled: false,
            exception: new TargetInvocationException(
                new ScheduledJobRecordedFailureException(
                    "job-key",
                    Guid.NewGuid(),
                    terminalStateRecorded: true,
                    failureNotificationHandled: true)));

        filter.OnPerformed(context);

        scopeFactory.DidNotReceive().CreateScope();
    }

    [Fact]
    public void ExecutionFilter遇到Runner正常成功不得误补失败历史()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var filter = new ScheduledJobExecutionFilter(
            scopeFactory,
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>());
        var job = CreateJob(typeof(DirectRunnerJob));
        var backgroundJob = new BackgroundJob("job-success", job, DateTime.UtcNow);
        var performContext = new PerformContext(
            Substitute.For<JobStorage>(),
            Substitute.For<IStorageConnection>(),
            backgroundJob,
            Substitute.For<IJobCancellationToken>());
        var context = new PerformedContext(
            performContext,
            result: null,
            canceled: false,
            exception: null);

        filter.OnPerformed(context);

        scopeFactory.DidNotReceive().CreateScope();
    }

    [Fact]
    public void ExecutionFilter遇到未确认落库的Typed失败必须补历史()
    {
        var originalExecutionId = Guid.NewGuid();
        var recorder = Substitute.For<IScheduledJobExecutionRecorder>();
        recorder
            .FinishAsync(
                originalExecutionId,
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(true);
        using var services = new ServiceCollection()
            .AddSingleton(recorder)
            .BuildServiceProvider();
        var filter = new ScheduledJobExecutionFilter(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>());
        var context = CreatePerformedContext(
            CreateJob(typeof(DirectRunnerJob)),
            new ScheduledJobRecordedFailureException(
                "job-key",
                originalExecutionId,
                terminalStateRecorded: false,
                failureNotificationHandled: false));

        filter.OnPerformed(context);

        recorder.Received(1).FinishAsync(
            originalExecutionId,
            false,
            Arg.Any<string?>(),
            Arg.Any<string?>());
        recorder.DidNotReceive().StartAsync(
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<JobTriggerKind>(),
            Arg.Any<bool>(),
            Arg.Any<string?>());
    }

    [Fact]
    public void ExecutionFilter取消必须补Interrupted而非Failed()
    {
        var originalExecutionId = Guid.NewGuid();
        var recorder = Substitute.For<IScheduledJobExecutionRecorder>();
        recorder
            .InterruptAsync(originalExecutionId, Arg.Any<string?>())
            .Returns(true);
        using var services = new ServiceCollection()
            .AddSingleton(recorder)
            .BuildServiceProvider();
        var filter = new ScheduledJobExecutionFilter(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>());
        var context = CreatePerformedContext(
            CreateJob(typeof(DirectRunnerJob)),
            new ScheduledJobRecordedCancellationException(
                "job-key",
                originalExecutionId,
                terminalStateRecorded: false,
                cancellationToken: new CancellationToken(canceled: true)));

        filter.OnPerformed(context);

        recorder.Received(1).InterruptAsync(
            originalExecutionId,
            Arg.Any<string?>());
        recorder.DidNotReceive().FinishAsync(
            Arg.Any<Guid>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<string?>());
    }

    [Fact]
    public void ExecutionFilter成功终态未确认必须按原Id补偿()
    {
        var originalExecutionId = Guid.NewGuid();
        var recorder = Substitute.For<IScheduledJobExecutionRecorder>();
        recorder
            .FinishAsync(
                originalExecutionId,
                true,
                Arg.Any<string?>(),
                Arg.Is<string?>(value => value == null))
            .Returns(true);
        using var services = new ServiceCollection()
            .AddSingleton(recorder)
            .BuildServiceProvider();
        var filter = new ScheduledJobExecutionFilter(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>());
        var context = CreatePerformedContext(
            CreateJob(typeof(DirectRunnerJob)),
            new ScheduledJobUnconfirmedSuccessException(
                "job-key",
                originalExecutionId));

        filter.OnPerformed(context);

        recorder.Received(1).FinishAsync(
            originalExecutionId,
            true,
            Arg.Any<string?>(),
            Arg.Is<string?>(value => value == null));
        recorder.DidNotReceive().StartAsync(
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<JobTriggerKind>(),
            Arg.Any<bool>(),
            Arg.Any<string?>());
    }

    [Fact]
    public void ExecutionFilter跳过终态未确认必须按原Id补偿为Skipped()
    {
        var originalExecutionId = Guid.NewGuid();
        var recorder = Substitute.For<IScheduledJobExecutionRecorder>();
        recorder
            .SkipAsync(originalExecutionId, Arg.Any<string?>())
            .Returns(true);
        using var services = new ServiceCollection()
            .AddSingleton(recorder)
            .BuildServiceProvider();
        var filter = new ScheduledJobExecutionFilter(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>());
        var context = CreatePerformedContext(
            CreateJob(typeof(DirectRunnerJob)),
            new ScheduledJobUnconfirmedSkippedException(
                "job-key",
                originalExecutionId));

        filter.OnPerformed(context);

        recorder.Received(1).SkipAsync(
            originalExecutionId,
            Arg.Is<string?>(value => value != null && value.Contains("Skipped")));
        recorder.DidNotReceive().FinishAsync(
            Arg.Any<Guid>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<string?>());
        recorder.DidNotReceive().StartAsync(
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<JobTriggerKind>(),
            Arg.Any<bool>(),
            Arg.Any<string?>());
    }

    [Fact]
    public void ExecutionFilter跳过原记录补偿失败后必须新建Skipped记录()
    {
        var originalExecutionId = Guid.NewGuid();
        var fallbackExecutionId = Guid.NewGuid();
        var recorder = Substitute.For<IScheduledJobExecutionRecorder>();
        recorder
            .SkipAsync(originalExecutionId, Arg.Any<string?>())
            .Returns(false);
        recorder
            .StartAsync(
                "stable-skipped-key",
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<JobTriggerKind>(),
                Arg.Any<bool>(),
                Arg.Any<string?>())
            .Returns(new ScheduledJobExecutionStartResult(
                fallbackExecutionId,
                Recorded: false));
        recorder
            .SkipAsync(fallbackExecutionId, Arg.Any<string?>())
            .Returns(true);
        using var services = new ServiceCollection()
            .AddSingleton(recorder)
            .BuildServiceProvider();
        var filter = new ScheduledJobExecutionFilter(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>());
        var context = CreatePerformedContext(
            CreateJob(typeof(DirectRunnerJob)),
            new ScheduledJobUnconfirmedSkippedException(
                "stable-skipped-key",
                originalExecutionId));

        filter.OnPerformed(context);

        Received.InOrder(() =>
        {
            recorder.SkipAsync(
                originalExecutionId,
                Arg.Any<string?>());
            recorder.StartAsync(
                "stable-skipped-key",
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<JobTriggerKind>(),
                Arg.Any<bool>(),
                Arg.Any<string?>());
            recorder.SkipAsync(
                fallbackExecutionId,
                Arg.Any<string?>());
        });
    }

    [Fact]
    public void ExecutionFilter原记录补偿失败后才允许按Marker任务Key新建()
    {
        var originalExecutionId = Guid.NewGuid();
        var fallbackExecutionId = Guid.NewGuid();
        var recorder = Substitute.For<IScheduledJobExecutionRecorder>();
        recorder
            .FinishAsync(
                originalExecutionId,
                false,
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(false);
        recorder
            .StartAsync(
                "stable-job-key",
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<JobTriggerKind>(),
                Arg.Any<bool>(),
                Arg.Any<string?>())
            .Returns(new ScheduledJobExecutionStartResult(
                fallbackExecutionId,
                Recorded: false));
        recorder
            .FinishAsync(
                fallbackExecutionId,
                false,
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(true);
        using var services = new ServiceCollection()
            .AddSingleton(recorder)
            .BuildServiceProvider();
        var filter = new ScheduledJobExecutionFilter(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>());
        var context = CreatePerformedContext(
            CreateJob(typeof(DirectRunnerJob)),
            new ScheduledJobRecordedFailureException(
                "stable-job-key",
                originalExecutionId,
                terminalStateRecorded: false,
                failureNotificationHandled: false));

        filter.OnPerformed(context);

        Received.InOrder(() =>
        {
            recorder.FinishAsync(
                originalExecutionId,
                false,
                Arg.Any<string?>(),
                Arg.Any<string?>());
            recorder.StartAsync(
                "stable-job-key",
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<JobTriggerKind>(),
                Arg.Any<bool>(),
                Arg.Any<string?>());
            recorder.FinishAsync(
                fallbackExecutionId,
                false,
                Arg.Any<string?>(),
                Arg.Any<string?>());
        });
    }

    [Fact]
    public void ExecutionFilter取消Fallback的Start未确认也必须尝试Interrupt预生成Id()
    {
        var originalExecutionId = Guid.NewGuid();
        var fallbackExecutionId = Guid.NewGuid();
        var recorder = Substitute.For<IScheduledJobExecutionRecorder>();
        recorder
            .InterruptAsync(originalExecutionId, Arg.Any<string?>())
            .Returns(false);
        recorder
            .StartAsync(
                "stable-cancellation-key",
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<JobTriggerKind>(),
                Arg.Any<bool>(),
                Arg.Any<string?>())
            .Returns(new ScheduledJobExecutionStartResult(
                fallbackExecutionId,
                Recorded: false));
        recorder
            .InterruptAsync(fallbackExecutionId, Arg.Any<string?>())
            .Returns(true);
        using var services = new ServiceCollection()
            .AddSingleton(recorder)
            .BuildServiceProvider();
        var filter = new ScheduledJobExecutionFilter(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>());
        var context = CreatePerformedContext(
            CreateJob(typeof(DirectRunnerJob)),
            new ScheduledJobRecordedCancellationException(
                "stable-cancellation-key",
                originalExecutionId,
                terminalStateRecorded: false,
                cancellationToken: new CancellationToken(canceled: true)));

        filter.OnPerformed(context);

        Received.InOrder(() =>
        {
            recorder.InterruptAsync(
                originalExecutionId,
                Arg.Any<string?>());
            recorder.StartAsync(
                "stable-cancellation-key",
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<JobTriggerKind>(),
                Arg.Any<bool>(),
                Arg.Any<string?>());
            recorder.InterruptAsync(
                fallbackExecutionId,
                Arg.Any<string?>());
        });
    }

    [Fact]
    public void 纯取消Marker必须选举为Deleted_混合异常保持Failed()
    {
        var filter = new RunnerDrivenCancellationStateFilter();
        var cancellation = new ScheduledJobRecordedCancellationException(
            "job-key",
            Guid.NewGuid(),
            terminalStateRecorded: true,
            cancellationToken: new CancellationToken(canceled: true));
        var pureContext = new ElectStateContext(CreateApplyStateContext(
            new FailedState(new TargetInvocationException(cancellation))));

        filter.OnStateElection(pureContext);

        var deleted = pureContext.CandidateState.ShouldBeOfType<DeletedState>();
        deleted.Reason.ShouldContain("Interrupted");

        var mixedContext = new ElectStateContext(CreateApplyStateContext(
            new FailedState(new AggregateException(
                cancellation,
                new InvalidOperationException("real failure")))));

        filter.OnStateElection(mixedContext);

        mixedContext.CandidateState.ShouldBeOfType<FailedState>();

        var rawCancellationContext = new ElectStateContext(CreateApplyStateContext(
            new FailedState(new OperationCanceledException("raw cancellation"))));

        filter.OnStateElection(rawCancellationContext);

        rawCancellationContext.CandidateState.ShouldBeOfType<FailedState>();

        var aggregateRawCancellationContext = new ElectStateContext(
            CreateApplyStateContext(
                new FailedState(new AggregateException(
                    new OperationCanceledException("wrapped raw cancellation")))));

        filter.OnStateElection(aggregateRawCancellationContext);

        aggregateRawCancellationContext.CandidateState.ShouldBeOfType<FailedState>();
    }

    [Fact]
    public void Recurring生产入口停机取消必须保持Marker且不重复补历史()
    {
        AssertShutdownCancellationBoundary(
            ScheduledJobKeys.AuditLogCleanup,
            JobTriggerKind.Scheduled,
            argsJson: null,
            runner =>
            {
                var instance = new AuditLogCleanupJob(runner);
                var method = typeof(AuditLogCleanupJob).GetMethod(
                    nameof(AuditLogCleanupJob.ExecuteAsync),
                    [typeof(CancellationToken)])
                    ?? throw new InvalidOperationException("缺少 recurring 生产入口");
                return (
                    instance,
                    new Job(
                        typeof(AuditLogCleanupJob),
                        method,
                        new object[] { CancellationToken.None }));
            });
    }

    [Fact]
    public void 手工生产入口停机取消必须保持Marker且不重复补历史()
    {
        const string jobKey = "manual-shutdown-probe";
        const string argsJson = "{\"probe\":true}";
        AssertShutdownCancellationBoundary(
            jobKey,
            JobTriggerKind.Manual,
            argsJson,
            runner =>
            {
                var instance = new RunScheduledJobHangfireJob(runner);
                var method = typeof(RunScheduledJobHangfireJob).GetMethod(
                    nameof(RunScheduledJobHangfireJob.ExecuteAsync),
                    [typeof(RunScheduledJobArgs), typeof(CancellationToken)])
                    ?? throw new InvalidOperationException("缺少手工生产入口");
                return (
                    instance,
                    new Job(
                        typeof(RunScheduledJobHangfireJob),
                        method,
                        new object[]
                        {
                            new RunScheduledJobArgs
                            {
                                JobKey = jobKey,
                                ArgsJson = argsJson,
                            },
                            CancellationToken.None,
                        }));
            });
    }

    [Fact]
    public void Aggregate诊断必须覆盖全部叶并去重截断()
    {
        var duplicateA = new InvalidOperationException("same\r\nmessage");
        var duplicateB = new InvalidOperationException("same\r\nmessage");
        var longLeaf = new ArgumentException(new string('x', 800));
        var exception = new AggregateException(
            duplicateA,
            new TargetInvocationException(duplicateB),
            longLeaf);

        var description = ScheduledJobRecordedFailureInspector.DescribeLeaves(
            exception,
            maxLength: 700);

        description.Length.ShouldBeLessThanOrEqualTo(700);
        description.ShouldNotContain("\r");
        description.ShouldNotContain("\n");
        description.Split(
                "InvalidOperationException:",
                StringSplitOptions.None)
            .Length
            .ShouldBe(2);
        description.ShouldContain("ArgumentException:");
        description.ShouldEndWith("…");
    }

    [Fact]
    public void Aggregate诊断必须先去重再取十六个唯一根因并准确标记截断()
    {
        var repeatedThenDistinct = new AggregateException(
            Enumerable
                .Range(0, 20)
                .Select(_ => new InvalidOperationException("same root"))
                .Cast<Exception>()
                .Append(new ArgumentException("distinct root")));

        var deduplicated = ScheduledJobRecordedFailureInspector.DescribeLeaves(
            repeatedThenDistinct);

        deduplicated.ShouldContain("InvalidOperationException: same root");
        deduplicated.ShouldContain("ArgumentException: distinct root");
        deduplicated.ShouldNotContain("异常叶已截断");

        var seventeenUnique = new AggregateException(
            Enumerable
                .Range(0, 17)
                .Select(index => new InvalidOperationException($"unique root {index}")));

        var truncated = ScheduledJobRecordedFailureInspector.DescribeLeaves(
            seventeenUnique);

        truncated.ShouldContain("InvalidOperationException: unique root 15");
        truncated.ShouldNotContain("InvalidOperationException: unique root 16");
        truncated.ShouldContain("异常叶已截断");
    }

    [Fact]
    public void NotificationFilter遇到已记录失败仍进入Failed但不得再发告警()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var filter = new JobFailureNotificationFilter(
            scopeFactory,
            Substitute.For<ILogger<JobFailureNotificationFilter>>());
        var failed = new FailedState(new AggregateException(
            new ScheduledJobRecordedFailureException(
                "job-key",
                Guid.NewGuid(),
                terminalStateRecorded: true,
                failureNotificationHandled: true)));
        var backgroundJob = new BackgroundJob(
            "job-2",
            CreateJob(typeof(DirectRunnerJob)),
            DateTime.UtcNow);
        var transaction = Substitute.For<IWriteOnlyTransaction>();
        var context = new ApplyStateContext(
            Substitute.For<JobStorage>(),
            Substitute.For<IStorageConnection>(),
            transaction,
            backgroundJob,
            failed,
            oldStateName: null);

        filter.OnStateApplied(context, transaction);

        scopeFactory.DidNotReceive().CreateScope();
    }

    [Fact]
    public async Task NotificationFilter遇到业务告警未送达必须触发HangfireFallback()
    {
        var sent = new TaskCompletionSource<NotificationSendRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var notification = Substitute.For<INotificationService>();
        notification
            .SendAsync(
                Arg.Any<NotificationSendRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sent.TrySetResult(call.Arg<NotificationSendRequest>());
                return Task.FromResult(NotificationSendResult.Ok());
            });
        using var services = new ServiceCollection()
            .AddSingleton(notification)
            .BuildServiceProvider();
        var filter = new JobFailureNotificationFilter(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<JobFailureNotificationFilter>>());
        var failed = new FailedState(new ScheduledJobRecordedFailureException(
            "job-key",
            Guid.NewGuid(),
            terminalStateRecorded: true,
            failureNotificationHandled: false));
        var backgroundJob = new BackgroundJob(
            "job-notification-fallback",
            CreateJob(typeof(DirectRunnerJob)),
            DateTime.UtcNow);
        var transaction = Substitute.For<IWriteOnlyTransaction>();
        var context = new ApplyStateContext(
            Substitute.For<JobStorage>(),
            Substitute.For<IStorageConnection>(),
            transaction,
            backgroundJob,
            failed,
            oldStateName: null);

        filter.OnStateApplied(context, transaction);

        var request = await sent.Task.WaitAsync(TimeSpan.FromSeconds(3));
        request.Source.ShouldBe(NotificationSources.Hangfire);
        request.Level.ShouldBe(NotificationLevel.Critical);
    }

    private static Job CreateJob(Type jobType)
    {
        var method = jobType.GetMethod(nameof(DirectRunnerJob.ExecuteAsync))
            ?? throw new InvalidOperationException("测试作业缺少 ExecuteAsync");
        return new Job(
            jobType,
            method,
            new object[] { CancellationToken.None });
    }

    private static PerformedContext CreatePerformedContext(
        Job job,
        Exception exception,
        string? recurringJobId = null)
    {
        var backgroundJob = new BackgroundJob("job-fallback", job, DateTime.UtcNow);
        var storedParameter = (Name: (string?)null, Value: (string?)null);
        var connection = Substitute.For<IStorageConnection>();
        connection
            .When(candidate => candidate.SetJobParameter(
                "job-fallback",
                Arg.Any<string>(),
                Arg.Any<string>()))
            .Do(call => storedParameter = (
                call.ArgAt<string>(1),
                call.ArgAt<string>(2)));
        connection
            .GetJobParameter("job-fallback", Arg.Any<string>())
            .Returns(call => storedParameter.Name == call.ArgAt<string>(1)
                ? storedParameter.Value
                : null);
        var performContext = new PerformContext(
            Substitute.For<JobStorage>(),
            connection,
            backgroundJob,
            Substitute.For<IJobCancellationToken>());
        if (!string.IsNullOrWhiteSpace(recurringJobId))
        {
            performContext.SetJobParameter("RecurringJobId", recurringJobId);
        }

        return new PerformedContext(
            performContext,
            result: null,
            canceled: false,
            exception);
    }

    private static ApplyStateContext CreateApplyStateContext(IState candidateState)
    {
        var transaction = Substitute.For<IWriteOnlyTransaction>();
        return new ApplyStateContext(
            Substitute.For<JobStorage>(),
            Substitute.For<IStorageConnection>(),
            transaction,
            new BackgroundJob(
                "job-election",
                CreateJob(typeof(DirectRunnerJob)),
                DateTime.UtcNow),
            candidateState,
            oldStateName: null);
    }

    private static void AssertShutdownCancellationBoundary(
        string jobKey,
        JobTriggerKind triggerKind,
        string? argsJson,
        Func<ScheduledJobRunner, (object Instance, Job Job)> createEntry)
    {
        var executionId = Guid.NewGuid();
        var handler = new CancellationBoundaryHandler(jobKey);
        var resolver = Substitute.For<IScheduledJobHandlerResolver>();
        resolver.Find(jobKey).Returns(handler);
        var recorder = Substitute.For<IScheduledJobExecutionRecorder>();
        recorder
            .StartAsync(
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<JobTriggerKind>(),
                Arg.Any<bool>(),
                Arg.Any<string?>())
            .Returns(new ScheduledJobExecutionStartResult(executionId, Recorded: true));
        recorder
            .InterruptAsync(executionId, Arg.Any<string?>())
            .Returns(true);
        var runner = new ScheduledJobRunner(
            resolver,
            recorder,
            Substitute.For<INotificationService>(),
            Substitute.For<IRepository<ScheduledJobExecution, Guid>>(),
            new ConfigurationBuilder().Build(),
            Substitute.For<IUnitOfWorkManager>(),
            Substitute.For<ILogger<ScheduledJobRunner>>(),
            Substitute.For<IAsyncQueryableExecuter>());
        var (instance, job) = createEntry(runner);
        job.Method.GetCustomAttribute<AsyncStateMachineAttribute>().ShouldBeNull();

        using var services = new ServiceCollection()
            .AddSingleton(recorder)
            .BuildServiceProvider();
        var filters = new JobFilterCollection();
        filters.Add(new ScheduledJobExecutionFilter(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<ScheduledJobExecutionFilter>>()));
        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();
        var jobCancellation = Substitute.For<IJobCancellationToken>();
        jobCancellation.ShutdownToken.Returns(shutdown.Token);
        var context = new PerformContext(
            Substitute.For<JobStorage>(),
            Substitute.For<IStorageConnection>(),
            new BackgroundJob("job-shutdown-requeue", job, DateTime.UtcNow),
            jobCancellation);
        var performer = new BackgroundJobPerformer(
            filters,
            new FixedInstanceJobActivator(instance));

        var exception = Should.Throw<ScheduledJobRecordedCancellationException>(
            () => performer.Perform(context));

        exception.CancellationToken.ShouldBe(shutdown.Token);
        exception.TerminalStateRecorded.ShouldBeTrue();
        recorder.Received(1).StartAsync(
            Arg.Is<string?>(value => value == jobKey),
            Arg.Any<string>(),
            Arg.Is<string?>(value => value == "RunAsync"),
            Arg.Is<string?>(value => value == null),
            triggerKind,
            false,
            Arg.Is<string?>(value => value == argsJson));
        recorder.Received(1).StartAsync(
            Arg.Any<string?>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<JobTriggerKind>(),
            Arg.Any<bool>(),
            Arg.Any<string?>());
        recorder.Received(1).InterruptAsync(
            executionId,
            Arg.Any<string?>());
    }

    private static Type GetAdapterDefinition()
        => typeof(AbpBackgroundJobsHangfireModule)
            .Assembly
            .GetTypes()
            .Single(type => type.IsGenericTypeDefinition
                && type.FullName == "Volo.Abp.BackgroundJobs.Hangfire.HangfireJobExecutionAdapter`1");

    private static Job CreateAdapterJob(Type adapterType, object? jobArgs = null)
    {
        var method = adapterType.GetMethods()
            .Single(candidate => candidate.Name == "ExecuteAsync");
        var parameters = method.GetParameters();
        parameters.Select(parameter => parameter.ParameterType).ShouldBe(new[]
        {
            typeof(string),
            adapterType.GetGenericArguments().Single(),
            typeof(CancellationToken),
        });
        var args = jobArgs ?? Activator.CreateInstance(parameters[1].ParameterType)
            ?? throw new InvalidOperationException("无法构造 ABP adapter 参数");
        parameters[1].ParameterType.IsInstanceOfType(args).ShouldBeTrue();
        return new Job(adapterType, method, new object[]
        {
            "default",
            args,
            CancellationToken.None,
        });
    }

    private sealed class DirectRunnerJob : IRunnerDrivenJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OtherAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UnrelatedJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UnrelatedArgs
    {
    }

    private sealed class CancellationBoundaryHandler : IScheduledJobHandler
    {
        public CancellationBoundaryHandler(string jobKey)
        {
            JobKey = jobKey;
        }

        public string JobKey { get; }

        public bool UseUnitOfWork => false;

        public Task<JobRunResult> RunAsync(
            JobRunContext context,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "取消应在并发闸前被 Runner 观察，不应进入处理器");
    }

    private sealed class FixedInstanceJobActivator : JobActivator
    {
        private readonly object _instance;

        public FixedInstanceJobActivator(object instance)
        {
            _instance = instance;
        }

        public override object ActivateJob(Type jobType)
        {
            jobType.ShouldBe(_instance.GetType());
            return _instance;
        }
    }
}
