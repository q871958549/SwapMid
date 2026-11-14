using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Notification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Guids;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DataIntegration;

public sealed class PipelineFailureNotificationSchedulerTests
{
    [Fact]
    public async Task 终态事务内应直接耐久入队且参数不含异常正文()
    {
        const string secret = "Password=P1_NOTIFICATION_JOB_SECRET";
        var backgroundJobStore = Substitute.For<IBackgroundJobStore>();
        var serializer = Substitute.For<IBackgroundJobSerializer>();
        PipelineFailureNotificationJobArgs? captured = null;
        serializer.Serialize(Arg.Do<object>(value =>
                captured = value as PipelineFailureNotificationJobArgs))
            .Returns("safe-job-args");
        BackgroundJobInfo? jobInfo = null;
        backgroundJobStore
            .When(store => store.InsertAsync(Arg.Any<BackgroundJobInfo>()))
            .Do(call => jobInfo = call.Arg<BackgroundJobInfo>());
        var unitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        var terminalUnitOfWork = Substitute.For<IUnitOfWork>();
        terminalUnitOfWork.Options.Returns(new AbpUnitOfWorkOptions
        {
            IsTransactional = true,
        });
        unitOfWorkManager.Current.Returns(terminalUnitOfWork);
        var jobId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc);
        var scheduler = CreateScheduler(
            backgroundJobStore,
            serializer,
            unitOfWorkManager,
            jobId,
            now);
        var pipelineId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var failure = new PipelineExecutionFailurePolicy.FailureDescriptor(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionFailed,
            $"provider failed;{secret}",
            "correlation-123",
            typeof(InvalidOperationException).FullName!);

        await scheduler.ScheduleAsync(
            pipelineId,
            executionId,
            failure);

        captured.ShouldNotBeNull();
        captured.PipelineId.ShouldBe(pipelineId);
        captured.ExecutionId.ShouldBe(executionId);
        captured.ErrorCode.ShouldBe(failure.ErrorCode);
        captured.CorrelationId.ShouldBe(failure.CorrelationId);
        captured.ExceptionType.ShouldBe(failure.ExceptionType);
        captured.OutcomeUnknown.ShouldBeFalse();
        jobInfo.ShouldNotBeNull();
        jobInfo.Id.ShouldBe(jobId);
        jobInfo.ApplicationName.ShouldBe("SwapMid.Tests");
        jobInfo.JobName.ShouldBe(
            BackgroundJobNameAttribute.GetName<
                PipelineFailureNotificationJobArgs>());
        jobInfo.JobArgs.ShouldBe("safe-job-args");
        jobInfo.CreationTime.ShouldBe(now);
        jobInfo.NextTryTime.ShouldBe(now);
        typeof(PipelineFailureNotificationJobArgs)
            .GetProperties()
            .Select(property => property.Name)
            .ShouldNotContain("SafeMessage");
        JsonSerializer.Serialize(captured).ShouldNotContain(secret);
        terminalUnitOfWork.DidNotReceive()
            .OnCompleted(Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task 缺少终态事务时应失败关闭且不得入队()
    {
        var backgroundJobStore = Substitute.For<IBackgroundJobStore>();
        var serializer = Substitute.For<IBackgroundJobSerializer>();
        var unitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        unitOfWorkManager.Current.Returns((IUnitOfWork?)null);
        var scheduler = CreateScheduler(
            backgroundJobStore,
            serializer,
            unitOfWorkManager);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            scheduler.ScheduleAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                CreateFailure()));

        await backgroundJobStore.DidNotReceiveWithAnyArgs()
            .InsertAsync(default!);
    }

    [Fact]
    public async Task 非事务Uow内应失败关闭且不得入队()
    {
        var backgroundJobStore = Substitute.For<IBackgroundJobStore>();
        var serializer = Substitute.For<IBackgroundJobSerializer>();
        var unitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        var nonTransactionalUnitOfWork = Substitute.For<IUnitOfWork>();
        nonTransactionalUnitOfWork.Options.Returns(new AbpUnitOfWorkOptions
        {
            IsTransactional = false,
        });
        unitOfWorkManager.Current.Returns(nonTransactionalUnitOfWork);
        var scheduler = CreateScheduler(
            backgroundJobStore,
            serializer,
            unitOfWorkManager);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            scheduler.ScheduleAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                CreateFailure()));

        await backgroundJobStore.DidNotReceiveWithAnyArgs()
            .InsertAsync(default!);
    }

    [Fact]
    public async Task 终态存储拓扑异常时应在序列化与入队前失败关闭()
    {
        var backgroundJobStore = Substitute.For<IBackgroundJobStore>();
        var serializer = Substitute.For<IBackgroundJobSerializer>();
        var unitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        var terminalUnitOfWork = Substitute.For<IUnitOfWork>();
        terminalUnitOfWork.Options.Returns(new AbpUnitOfWorkOptions
        {
            IsTransactional = true,
        });
        unitOfWorkManager.Current.Returns(terminalUnitOfWork);
        var topologyGuard =
            Substitute.For<IPipelineTerminalStoreTopologyGuard>();
        topologyGuard.When(value => value.EnsureSafeScope(
                PipelineTerminalStoreTopologyPhase.NotificationSchedule))
            .Do(_ => throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionCoordinationUnavailable));
        var scheduler = CreateScheduler(
            backgroundJobStore,
            serializer,
            unitOfWorkManager,
            topologyGuard: topologyGuard);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            scheduler.ScheduleAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                CreateFailure()));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        serializer.DidNotReceiveWithAnyArgs().Serialize(default!);
        await backgroundJobStore.DidNotReceiveWithAnyArgs()
            .InsertAsync(default!);
    }

    [Fact]
    public async Task 后台任务应只发送稳定错误证据()
    {
        NotificationSendRequest? captured = null;
        var notificationService = Substitute.For<INotificationService>();
        notificationService.SendAsync(
                Arg.Do<NotificationSendRequest>(request => captured = request),
                Arg.Any<CancellationToken>())
            .Returns(NotificationSendResult.Ok());
        var unitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWorkManager
            .Begin(Arg.Any<AbpUnitOfWorkOptions>(), true)
            .Returns(unitOfWork);
        var cancellationTokenProvider =
            Substitute.For<ICancellationTokenProvider>();
        cancellationTokenProvider.Token.Returns(CancellationToken.None);
        var job = new PipelineFailureNotificationJob(
            notificationService,
            unitOfWorkManager,
            cancellationTokenProvider,
            Options.Create(new EtlExecutionOptions()),
            NullLogger<PipelineFailureNotificationJob>.Instance);
        var args = CreateArgs();

        await job.ExecuteAsync(args);

        captured.ShouldNotBeNull();
        captured.Title.ShouldBe("ETL 管道执行失败");
        captured.Content.ShouldContain(args.PipelineId.ToString("N"));
        captured.Content.ShouldContain(args.ExecutionId.ToString("N"));
        captured.Content.ShouldContain(args.ErrorCode);
        captured.Content.ShouldContain(args.CorrelationId);
        captured.Content.ShouldContain(args.ExceptionType);
        captured.Content.ShouldNotContain("Password=");
        captured.Source.ShouldBe(NotificationSources.DataIntegration);
        await unitOfWork.Received(1)
            .CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 通知渠道遵守取消时应在配置期限内释放后台工作器()
    {
        CancellationToken capturedDeliveryToken = default;
        var notificationService = Substitute.For<INotificationService>();
        notificationService.SendAsync(
                Arg.Any<NotificationSendRequest>(),
                Arg.Do<CancellationToken>(token =>
                    capturedDeliveryToken = token))
            .Returns(call => WaitForCancellationAsync(
                call.Arg<CancellationToken>()));
        var unitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWorkManager
            .Begin(Arg.Any<AbpUnitOfWorkOptions>(), true)
            .Returns(unitOfWork);
        var cancellationTokenProvider =
            Substitute.For<ICancellationTokenProvider>();
        cancellationTokenProvider.Token.Returns(CancellationToken.None);
        var job = new PipelineFailureNotificationJob(
            notificationService,
            unitOfWorkManager,
            cancellationTokenProvider,
            Options.Create(new EtlExecutionOptions
            {
                NotificationDeliveryTimeoutSeconds = 1,
            }),
            NullLogger<PipelineFailureNotificationJob>.Instance);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<
            PipelineFailureNotificationDeliveryException>(async () =>
                await job.ExecuteAsync(CreateArgs())
                    .WaitAsync(TimeSpan.FromSeconds(8)));

        stopwatch.Stop();
        // 配置超时是 1 秒。自托管机器挤在一起跑时，取消被观察到的时间会超过 3 秒。
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(6));
        capturedDeliveryToken.IsCancellationRequested.ShouldBeTrue();
        exception.Message.ShouldContain("delivery_timeout");
        await unitOfWork.DidNotReceive()
            .CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 后台工作器停止取消应原样传播而非伪装为通知超时()
    {
        using var workerCancellation = new CancellationTokenSource();
        workerCancellation.Cancel();
        var notificationService = Substitute.For<INotificationService>();
        notificationService.SendAsync(
                Arg.Any<NotificationSendRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromCanceled<NotificationSendResult>(
                call.Arg<CancellationToken>()));
        var unitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        unitOfWorkManager
            .Begin(Arg.Any<AbpUnitOfWorkOptions>(), true)
            .Returns(Substitute.For<IUnitOfWork>());
        var cancellationTokenProvider =
            Substitute.For<ICancellationTokenProvider>();
        cancellationTokenProvider.Token.Returns(workerCancellation.Token);
        var job = new PipelineFailureNotificationJob(
            notificationService,
            unitOfWorkManager,
            cancellationTokenProvider,
            Options.Create(new EtlExecutionOptions()),
            NullLogger<PipelineFailureNotificationJob>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(
            () => job.ExecuteAsync(CreateArgs()));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void 通知期限配置只允许一至一百二十秒(
        int timeoutSeconds,
        bool expected)
    {
        var options = new EtlExecutionOptions
        {
            NotificationDeliveryTimeoutSeconds = timeoutSeconds,
        };

        EtlExecutionOptions.IsValid(options).ShouldBe(expected);
    }

    [Fact]
    public async Task 通知发送永不完成时终态入队与提交仍可完成()
    {
        var sendStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource<NotificationSendResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationService = Substitute.For<INotificationService>();
        notificationService.SendAsync(
                Arg.Any<NotificationSendRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sendStarted.TrySetResult();
                return releaseSend.Task;
            });
        var jobUnitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        jobUnitOfWorkManager
            .Begin(Arg.Any<AbpUnitOfWorkOptions>(), true)
            .Returns(Substitute.For<IUnitOfWork>());
        var cancellationTokenProvider =
            Substitute.For<ICancellationTokenProvider>();
        cancellationTokenProvider.Token.Returns(CancellationToken.None);
        var job = new PipelineFailureNotificationJob(
            notificationService,
            jobUnitOfWorkManager,
            cancellationTokenProvider,
            Options.Create(new EtlExecutionOptions()),
            NullLogger<PipelineFailureNotificationJob>.Instance);
        var sendTask = job.ExecuteAsync(CreateArgs());
        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        sendTask.IsCompleted.ShouldBeFalse();

        var backgroundJobStore = Substitute.For<IBackgroundJobStore>();
        var serializer = Substitute.For<IBackgroundJobSerializer>();
        serializer.Serialize(Arg.Any<object>()).Returns("safe-job-args");
        var terminalUnitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        var terminalUnitOfWork = Substitute.For<IUnitOfWork>();
        terminalUnitOfWork.Options.Returns(new AbpUnitOfWorkOptions
        {
            IsTransactional = true,
        });
        terminalUnitOfWorkManager.Current.Returns(terminalUnitOfWork);
        var scheduler = CreateScheduler(
            backgroundJobStore,
            serializer,
            terminalUnitOfWorkManager);

        await scheduler.ScheduleAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                CreateFailure())
            .WaitAsync(TimeSpan.FromSeconds(1));
        await terminalUnitOfWork.CompleteAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));
        sendTask.IsCompleted.ShouldBeFalse(
            "独立后台通知黑洞不得回流阻塞终态事务或延长 ETL fence");

        releaseSend.SetResult(NotificationSendResult.Ok());
        await sendTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    private static PipelineFailureNotificationScheduler CreateScheduler(
        IBackgroundJobStore backgroundJobStore,
        IBackgroundJobSerializer serializer,
        IUnitOfWorkManager unitOfWorkManager,
        Guid? jobId = null,
        DateTime? now = null,
        IPipelineTerminalStoreTopologyGuard? topologyGuard = null)
    {
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(jobId ?? Guid.NewGuid());
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(now ?? DateTime.UtcNow);
        if (topologyGuard == null)
        {
            topologyGuard =
                Substitute.For<IPipelineTerminalStoreTopologyGuard>();
        }
        return new PipelineFailureNotificationScheduler(
            backgroundJobStore,
            serializer,
            guidGenerator,
            clock,
            Options.Create(new AbpBackgroundJobWorkerOptions
            {
                ApplicationName = "SwapMid.Tests",
            }),
            new PipelineTerminalUnitOfWorkGuard(unitOfWorkManager),
            topologyGuard);
    }

    private static PipelineExecutionFailurePolicy.FailureDescriptor
        CreateFailure()
        => new(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionFailed,
            "安全说明",
            "correlation-123",
            typeof(InvalidOperationException).FullName!);

    private static PipelineFailureNotificationJobArgs CreateArgs()
        => new()
        {
            PipelineId = Guid.NewGuid(),
            ExecutionId = Guid.NewGuid(),
            ErrorCode = SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionFailed,
            CorrelationId = "correlation-123",
            ExceptionType = typeof(InvalidOperationException).FullName!,
        };

    private static async Task<NotificationSendResult>
        WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return NotificationSendResult.Ok();
    }
}
