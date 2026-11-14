using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using SwapMid.Notification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.Scheduling;

/// <summary>
/// <see cref="ScheduledJobRunner"/> 端到端集成测试（SQLite 内存库 + FakeNotification）：
/// 验证演练/正式执行历史记录、产出物分发三档语义（成功 / 配置性跳过不算失败 / 真失败记 Failed）、
/// 连续失败告警抑制与恢复通知、并发互斥闸以及工作单元边界。
/// </summary>
[Collection(EntityFrameworkCoreTestCollections.Scheduling)]
public class ScheduledJobRunnerTests : SwapMidEntityFrameworkCoreTestBase
{
    private const string JobKey = "Probe_Dispatch_Report";

    private readonly ProbeDispatchHandler _probeHandler;
    private readonly ScheduledJobRunner _runner;
    private readonly FakeNotificationService _notification;
    private readonly IRepository<ScheduledJobExecution, Guid> _executions;

    public ScheduledJobRunnerTests()
    {
        _notification = (FakeNotificationService)GetRequiredService<INotificationService>();
        _executions = GetRequiredService<IRepository<ScheduledJobExecution, Guid>>();
        _probeHandler = new ProbeDispatchHandler(JobKey);
        _runner = CreateRunner(
            _probeHandler,
            GetRequiredService<IUnitOfWorkManager>(),
            GetRequiredService<IScheduledJobExecutionRecorder>());
        _notification.Reset();
    }

    [Fact]
    public void 通知中心真实具体类型可以经容器解析_不发送通知()
        => GetRequiredService<NotificationService>().ShouldNotBeNull();

    [Fact]
    public async Task 事故边界成败同一开始时间_不得混并为旧事故恢复()
    {
        var key = "recovery-boundary-" + Guid.NewGuid().ToString("N");
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        var runner = CreateRunner(new FixedResultHandler(key, JobRunResult.Ok("真实成功")), uowManager,
            GetRequiredService<IScheduledJobExecutionRecorder>());
        var at = DateTime.Now.AddDays(-1);
        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var item in new[] { (at, false), (at, true), (at.AddSeconds(1), false) })
            {
                var execution = new ScheduledJobExecution(Guid.NewGuid(), key, "test", "RunAsync", null,
                    JobTriggerKind.Scheduled, item.Item1);
                execution.MarkFinished(item.Item1.AddMilliseconds(1), item.Item2, null, null);
                await _executions.InsertAsync(execution, autoSave: true);
            }
        });
        var result = await runner.RunAsync(key, new JobRunContext { TriggerKind = JobTriggerKind.Scheduled });
        result.Status.ShouldBe(JobExecutionStatus.Succeeded);
        _notification.Sent.ShouldNotContain(r => r.Title.StartsWith("定时任务已恢复"));
    }

    [Fact]
    public async Task RunAsync_演练_返回成功_记手动历史_不执行正式写操作()
    {
        const string month = "2032-03";

        var result = await WithUnitOfWorkAsync(() => _runner.RunAsync(JobKey, new JobRunContext
        {
            TriggerKind = JobTriggerKind.Manual,
            IsDryRun = true,
            TestRecipient = "test@example.com",
            ArgsJson = $"{{\"month\":\"{month}\"}}",
        }));

        result.Status.ShouldBe(JobExecutionStatus.Succeeded);
        result.Output.ShouldNotBeNull();
        result.Output!.Attachments.Count.ShouldBe(1);
        _probeHandler.FormalWriteCount.ShouldBe(0);

        await WithUnitOfWorkAsync(async () =>
        {
            var execs = await _executions.GetListAsync(x => x.JobKey == JobKey);
            execs.ShouldContain(e => e.TriggerKind == JobTriggerKind.Manual && e.Status == JobExecutionStatus.Succeeded
                && e.IsDryRun && e.ArgsJson == $"{{\"month\":\"{month}\"}}");
        });
    }

    [Fact]
    public async Task RunAsync_正式_执行写操作_记手动历史()
    {
        const string month = "2032-04";

        var result = await WithUnitOfWorkAsync(() => _runner.RunAsync(JobKey, new JobRunContext
        {
            TriggerKind = JobTriggerKind.Manual,
            IsDryRun = false,
            ArgsJson = $"{{\"month\":\"{month}\"}}",
        }));

        result.Status.ShouldBe(JobExecutionStatus.Succeeded);
        _probeHandler.FormalWriteCount.ShouldBe(1);

        await WithUnitOfWorkAsync(async () =>
        {
            var execs = await _executions.GetListAsync(x => x.JobKey == JobKey);
            execs.ShouldContain(e => e.TriggerKind == JobTriggerKind.Manual && e.Status == JobExecutionStatus.Succeeded);
        });
    }

    [Fact]
    public async Task RunAsync_分发真失败_整体记Failed_摘要注明()
    {
        const string month = "2032-07";
        _notification.Behavior = _ => NotificationSendResult.Fail("SMTP 连接失败");

        var result = await WithUnitOfWorkAsync(() => _runner.RunAsync(JobKey, new JobRunContext
        {
            TriggerKind = JobTriggerKind.Manual,
            IsDryRun = true,
            TestRecipient = "test@example.com",
            ArgsJson = $"{{\"month\":\"{month}\"}}",
        }));

        result.Status.ShouldBe(JobExecutionStatus.Failed);
        result.Summary.ShouldNotBeNull();
        result.Summary!.ShouldContain("产出分发失败");
        result.Summary!.ShouldContain("SMTP 连接失败");

        await WithUnitOfWorkAsync(async () =>
        {
            var execs = await _executions.GetListAsync(x => x.JobKey == JobKey);
            execs.ShouldContain(e => e.Status == JobExecutionStatus.Failed
                && e.Summary != null && e.Summary.Contains("产出分发失败"));
        });
    }

    [Fact]
    public async Task RunAsync_分发配置性跳过_仍记成功_摘要注明()
    {
        const string month = "2032-08";
        _notification.Behavior = _ => NotificationSendResult.Skip("没有已启用的通知渠道");

        var result = await WithUnitOfWorkAsync(() => _runner.RunAsync(JobKey, new JobRunContext
        {
            TriggerKind = JobTriggerKind.Manual,
            IsDryRun = true,
            TestRecipient = "test@example.com",
            ArgsJson = $"{{\"month\":\"{month}\"}}",
        }));

        result.Status.ShouldBe(JobExecutionStatus.Succeeded);
        result.Summary.ShouldNotBeNull();
        result.Summary!.ShouldContain("分发跳过");

        await WithUnitOfWorkAsync(async () =>
        {
            var execs = await _executions.GetListAsync(x => x.JobKey == JobKey);
            execs.ShouldContain(e => e.Status == JobExecutionStatus.Succeeded
                && e.Summary != null && e.Summary.Contains("分发跳过"));
        });
    }

    [Fact]
    public async Task RunAsync_连续失败告警抑制_恢复发Info()
    {
        const string month = "2032-09";
        Task<JobRunResult> RunOnce() => WithUnitOfWorkAsync(() => _runner.RunAsync(JobKey, new JobRunContext
        {
            TriggerKind = JobTriggerKind.Scheduled,
            IsDryRun = false,
            ArgsJson = $"{{\"month\":\"{month}\"}}",
        }));

        _notification.Behavior = null;
        (await RunOnce()).Status.ShouldBe(JobExecutionStatus.Succeeded);

        _notification.Reset();
        _notification.Behavior = _ => NotificationSendResult.Fail("SMTP 连接失败");
        (await RunOnce()).Status.ShouldBe(JobExecutionStatus.Failed);
        _notification.Sent.ShouldContain(r => r.Title.StartsWith("定时任务失败"));
        var incidentTitle = _notification.Sent.Single(r => r.Title.StartsWith("定时任务失败")).Title;

        _notification.Reset();
        var gate = AcquireGateForTest(JobKey);
        try
        {
            (await RunOnce()).Status.ShouldBe(JobExecutionStatus.Skipped);
        }
        finally
        {
            gate.Release();
        }
        _notification.Sent.ShouldNotContain(r => r.Title.StartsWith("定时任务已恢复"));

        _notification.Reset();
        _notification.Behavior = _ => NotificationSendResult.Fail("SMTP 连接失败");
        (await RunOnce()).Status.ShouldBe(JobExecutionStatus.Failed);
        _notification.Sent.ShouldNotContain(r => r.Title.StartsWith("定时任务失败"));

        _notification.Reset();
        _notification.Behavior = null;
        (await RunOnce()).Status.ShouldBe(JobExecutionStatus.Succeeded);
        _notification.Sent.ShouldContain(r => r.Title.StartsWith("定时任务已恢复") && r.Level == NotificationLevel.Info);
        var recovery = _notification.Sent.Single(r => r.Title.StartsWith("定时任务已恢复"));
        recovery.RecoveryOfTitle.ShouldBe(incidentTitle);
        recovery.Content.ShouldContain("成功执行");
        recovery.Content.ShouldContain("不证明历史业务通知已补送");
        _notification.Reset();
        (await RunOnce()).Status.ShouldBe(JobExecutionStatus.Succeeded);
        _notification.Sent.ShouldNotContain(r => r.RecoveryOfTitle != null);

        _notification.Reset();
        _notification.Behavior = _ => NotificationSendResult.Fail("SMTP 再次故障");
        (await RunOnce()).Status.ShouldBe(JobExecutionStatus.Failed);
        var nextIncident = _notification.Sent.Single(r => r.Title.StartsWith("定时任务失败")).Title;
        nextIncident.ShouldNotBe(incidentTitle);
        _notification.Reset();
        (await RunOnce()).Status.ShouldBe(JobExecutionStatus.Succeeded);
        _notification.Sent.Single(r => r.RecoveryOfTitle != null).RecoveryOfTitle.ShouldBe(nextIncident);
    }

    [Fact]
    public async Task RunAsync_并发闸已占用_立即跳过不执行()
    {
        const string month = "2032-06";
        var gate = AcquireGateForTest(JobKey);
        try
        {
            var result = await WithUnitOfWorkAsync(() => _runner.RunAsync(JobKey, new JobRunContext
            {
                TriggerKind = JobTriggerKind.Scheduled,
                ArgsJson = $"{{\"month\":\"{month}\"}}",
            }));

            result.Status.ShouldBe(JobExecutionStatus.Skipped);
            result.Summary.ShouldBe("已有同任务执行中，本次跳过");
            _probeHandler.CallCount.ShouldBe(0);

            await WithUnitOfWorkAsync(async () =>
            {
                var execs = await _executions.GetListAsync(x => x.JobKey == JobKey);
                execs.ShouldContain(e => e.Status == JobExecutionStatus.Skipped
                    && e.Summary == "已有同任务执行中，本次跳过"
                    && e.Error == null);
            });
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task RunAsync_禁用工作单元时_Handler入口没有Ambient工作单元()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        var handler = new UnitOfWorkProbeHandler(
            "ScheduledJobRunner_NoAmbientUow",
            unitOfWorkManager,
            useUnitOfWork: false,
            isTransactional: false);
        var recorder = new ProbeExecutionRecorder();
        var runner = CreateRunner(handler, unitOfWorkManager, recorder);

        var result = await runner.RunAsync(
            handler.JobKey,
            new JobRunContext
            {
                TriggerKind = JobTriggerKind.Manual,
                IsDryRun = true,
            });

        result.Status.ShouldBe(JobExecutionStatus.Succeeded);
        handler.CallCount.ShouldBe(1);
        handler.ObservedCurrent.ShouldBeNull(
            "UseUnitOfWork=false 的处理器必须在数据库工作单元之外执行文件或网络副作用");
        unitOfWorkManager.Current.ShouldBeNull();
        recorder.FinishedSuccess.ShouldBe(true);
    }

    [Fact]
    public async Task RunAsync_并发闸已占用时_超长参数仍必须先失败关闭()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        var handler = new FixedResultHandler(
            "ScheduledJobRunner_ArgsLengthBoundary",
            JobRunResult.Ok("不应执行"));
        var recorder = new ProbeExecutionRecorder();
        var runner = CreateRunner(handler, unitOfWorkManager, recorder);

        var gate = AcquireGateForTest(handler.JobKey);
        try
        {
            var result = await runner.RunAsync(
                handler.JobKey,
                new JobRunContext
                {
                    TriggerKind = JobTriggerKind.Manual,
                    ArgsJson = new string(
                        ' ',
                        JobRunContext.MaxArgsJsonCharacters + 1),
                });

            result.Status.ShouldBe(JobExecutionStatus.Failed);
            result.Summary.ShouldNotBeNull();
            result.Summary!.ShouldContain("参数过长");
            result.Summary.ShouldNotContain("已有同任务");
            handler.CallCount.ShouldBe(0);
            recorder.FinishedSuccess.ShouldBe(false);
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task RunAsync_禁用工作单元但调用入口已有Ambient工作单元时_失败关闭且不调用Handler()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        var handler = new UnitOfWorkProbeHandler(
            "ScheduledJobRunner_RejectAmbientUow",
            unitOfWorkManager,
            useUnitOfWork: false,
            isTransactional: false);
        var recorder = new ProbeExecutionRecorder();
        var runner = CreateRunner(handler, unitOfWorkManager, recorder);

        var result = await WithUnitOfWorkAsync(async () =>
        {
            unitOfWorkManager.Current.ShouldNotBeNull();
            return await runner.RunAsync(
                handler.JobKey,
                new JobRunContext
                {
                    TriggerKind = JobTriggerKind.Manual,
                    IsDryRun = true,
                });
        });

        result.Status.ShouldBe(JobExecutionStatus.Failed);
        result.Summary.ShouldNotBeNull();
        result.Summary!.ShouldContain("要求无 ambient UoW");
        handler.CallCount.ShouldBe(
            0,
            "发现父工作单元后必须在任何处理器副作用发生前失败关闭");
        recorder.FinishedSuccess.ShouldBe(false);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_启用工作单元时_保持事务与非事务语义(bool isTransactional)
    {
        var unitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        IUnitOfWork? current = null;
        AbpUnitOfWorkOptions? observedOptions = null;
        unitOfWorkManager.Current.Returns(_ => current);
        unitOfWorkManager
            .Begin(Arg.Any<AbpUnitOfWorkOptions>(), true)
            .Returns(call =>
            {
                observedOptions = call.Arg<AbpUnitOfWorkOptions>();
                current = unitOfWork;
                return unitOfWork;
            });
        unitOfWork.Options.Returns(_ => observedOptions!);
        unitOfWork
            .CompleteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        unitOfWork
            .When(x => x.Dispose())
            .Do(_ => current = null);

        var handler = new UnitOfWorkProbeHandler(
            $"ScheduledJobRunner_WithUow_{isTransactional}",
            unitOfWorkManager,
            useUnitOfWork: true,
            isTransactional);
        var recorder = new ProbeExecutionRecorder();
        var runner = CreateRunner(handler, unitOfWorkManager, recorder);

        var result = await runner.RunAsync(
            handler.JobKey,
            new JobRunContext
            {
                TriggerKind = JobTriggerKind.Manual,
                IsDryRun = true,
            });

        result.Status.ShouldBe(JobExecutionStatus.Succeeded);
        handler.CallCount.ShouldBe(1);
        handler.ObservedCurrent.ShouldNotBeNull(
            "UseUnitOfWork=true 必须继续由 Runner 创建独立工作单元");
        observedOptions.ShouldNotBeNull();
        handler.ObservedIsTransactional.ShouldBe(
            isTransactional,
            "Runner 必须继续把 handler.IsTransactional 原样传给工作单元");
        unitOfWorkManager.Current.ShouldBeNull();
        recorder.FinishedSuccess.ShouldBe(true);
    }

    [Fact]
    public async Task RunAsync_失败继续返回兼容结果_RunForHangfire落终态后抛TypedException()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        var handler = new FixedResultHandler(
            "ScheduledJobRunner_TruthfulFailure",
            JobRunResult.Fail("底层敏感错误不应进入 Hangfire 异常"));

        var controlRecorder = new ProbeExecutionRecorder();
        var controlRunner = CreateRunner(handler, unitOfWorkManager, controlRecorder);
        var controlResult = await controlRunner.RunAsync(
            handler.JobKey,
            new JobRunContext { TriggerKind = JobTriggerKind.Manual });

        controlResult.Status.ShouldBe(JobExecutionStatus.Failed);
        controlRecorder.FinishedSuccess.ShouldBe(false);
        controlRecorder.FinishCount.ShouldBe(1);

        var hangfireRecorder = new ProbeExecutionRecorder();
        var hangfireRunner = CreateRunner(handler, unitOfWorkManager, hangfireRecorder);
        var exception = await Should.ThrowAsync<ScheduledJobRecordedFailureException>(
            () => hangfireRunner.RunForHangfireAsync(
                handler.JobKey,
                new JobRunContext { TriggerKind = JobTriggerKind.Scheduled }));

        exception.JobKey.ShouldBe(handler.JobKey);
        exception.ExecutionId.ShouldNotBe(Guid.Empty);
        exception.TerminalStateRecorded.ShouldBeTrue();
        exception.FailureNotificationHandled.ShouldBeTrue();
        exception.InnerException.ShouldBeNull();
        exception.Message.ShouldNotContain("底层敏感错误");
        hangfireRecorder.FinishedSuccess.ShouldBe(false);
        hangfireRecorder.FinishCount.ShouldBe(1);
    }

    [Fact]
    public async Task RunForHangfire_失败终态未确认落库时TypedException不得声称已记录()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        var handler = new FixedResultHandler(
            "ScheduledJobRunner_UnconfirmedFailure",
            JobRunResult.Fail("probe failure"));
        var recorder = new ProbeExecutionRecorder
        {
            StartPersistenceSucceeds = false,
        };
        var runner = CreateRunner(handler, unitOfWorkManager, recorder);

        var exception = await Should.ThrowAsync<ScheduledJobRecordedFailureException>(
            () => runner.RunForHangfireAsync(
                handler.JobKey,
                new JobRunContext { TriggerKind = JobTriggerKind.Scheduled }));

        exception.TerminalStateRecorded.ShouldBeFalse();
        exception.FailureNotificationHandled.ShouldBeFalse();
        exception.ExecutionId.ShouldNotBe(Guid.Empty);
        recorder.FinishCount.ShouldBe(1);
    }

    [Fact]
    public async Task RunForHangfire_业务告警返回失败时必须允许HangfireFallback()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        var handler = new FixedResultHandler(
            "ScheduledJobRunner_NotificationFallback",
            JobRunResult.Fail("probe failure"));
        var recorder = new ProbeExecutionRecorder();
        var runner = CreateRunner(handler, unitOfWorkManager, recorder);
        _notification.Behavior = request => request.Title.StartsWith("定时任务失败")
            ? NotificationSendResult.Fail("primary notification unavailable")
            : NotificationSendResult.Ok();

        var exception = await Should.ThrowAsync<ScheduledJobRecordedFailureException>(
            () => runner.RunForHangfireAsync(
                handler.JobKey,
                new JobRunContext { TriggerKind = JobTriggerKind.Scheduled }));

        exception.TerminalStateRecorded.ShouldBeTrue();
        exception.FailureNotificationHandled.ShouldBeFalse();
        _notification.Sent.ShouldContain(request =>
            request.Title.StartsWith("定时任务失败"));
        _notification.Sent.Single(request => request.Title.StartsWith("定时任务失败"))
            .Title.ShouldContain(exception.ExecutionId.ToString("N"));
    }

    [Fact]
    public async Task RunForHangfire_业务成功但终态未确认时不得静默成功或重放()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        var handler = new FixedResultHandler(
            "ScheduledJobRunner_UnconfirmedSuccess",
            JobRunResult.Ok("business side effect completed"));
        var recorder = new ProbeExecutionRecorder
        {
            TerminalPersistenceSucceeds = false,
        };
        var runner = CreateRunner(handler, unitOfWorkManager, recorder);

        var exception = await Should.ThrowAsync<ScheduledJobUnconfirmedSuccessException>(
            () => runner.RunForHangfireAsync(
                handler.JobKey,
                new JobRunContext { TriggerKind = JobTriggerKind.Scheduled }));

        exception.JobKey.ShouldBe(handler.JobKey);
        exception.ExecutionId.ShouldNotBe(Guid.Empty);
        exception.TerminalStateRecorded.ShouldBeFalse();
        exception.InnerException.ShouldBeNull();
        handler.CallCount.ShouldBe(1);
        recorder.FinishCount.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_Handler竞争结果必须记Skipped且不走Finish或通知()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        var handler = new FixedResultHandler(
            "ScheduledJobRunner_HandlerContended",
            JobRunResult.Contended("已有执行者，本次跳过"));
        var recorder = new ProbeExecutionRecorder();
        var runner = CreateRunner(handler, unitOfWorkManager, recorder);
        _notification.Reset();

        var result = await runner.RunAsync(
            handler.JobKey,
            new JobRunContext { TriggerKind = JobTriggerKind.Scheduled });

        result.Status.ShouldBe(JobExecutionStatus.Skipped);
        recorder.SkipCount.ShouldBe(1);
        recorder.SkippedSummary.ShouldBe("已有执行者，本次跳过");
        recorder.FinishCount.ShouldBe(0);
        _notification.Sent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(JobExecutionStatus.Running)]
    [InlineData(JobExecutionStatus.Interrupted)]
    public async Task RunAsync_Handler返回非终态必须失败关闭(
        JobExecutionStatus invalidStatus)
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        var handler = new FixedResultHandler(
            $"ScheduledJobRunner_InvalidStatus_{invalidStatus}",
            new JobRunResult
            {
                Status = invalidStatus,
                Summary = "不得被当作成功",
            });
        var recorder = new ProbeExecutionRecorder();
        var runner = CreateRunner(handler, unitOfWorkManager, recorder);

        var result = await runner.RunAsync(
            handler.JobKey,
            new JobRunContext
            {
                TriggerKind = JobTriggerKind.Manual,
                IsDryRun = true,
            });

        result.Status.ShouldBe(JobExecutionStatus.Failed);
        result.Summary.ShouldNotBeNull();
        result.Summary!.ShouldContain("非终态状态");
        recorder.FinishedSuccess.ShouldBe(false);
        recorder.FinishCount.ShouldBe(1);
        recorder.SkipCount.ShouldBe(0);
    }

    [Fact]
    public async Task RunForHangfire_跳过终态未确认时必须抛专用Marker且不重放业务()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        var handler = new FixedResultHandler(
            "ScheduledJobRunner_UnconfirmedSkipped",
            JobRunResult.Contended("已有执行者，本次跳过"));
        var recorder = new ProbeExecutionRecorder
        {
            TerminalPersistenceSucceeds = false,
        };
        var runner = CreateRunner(handler, unitOfWorkManager, recorder);

        var exception = await Should.ThrowAsync<ScheduledJobUnconfirmedSkippedException>(
            () => runner.RunForHangfireAsync(
                handler.JobKey,
                new JobRunContext { TriggerKind = JobTriggerKind.Scheduled }));

        exception.JobKey.ShouldBe(handler.JobKey);
        exception.ExecutionId.ShouldNotBe(Guid.Empty);
        exception.TerminalStateRecorded.ShouldBeFalse();
        exception.InnerException.ShouldBeNull();
        handler.CallCount.ShouldBe(1);
        recorder.SkipCount.ShouldBe(1);
        recorder.FinishCount.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_调用方取消必须穿透且历史已收尾()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        using var cancellation = new CancellationTokenSource();
        var handler = new CancelingHandler(
            "ScheduledJobRunner_Cancellation",
            cancellation);
        var recorder = new ProbeExecutionRecorder();
        var runner = CreateRunner(handler, unitOfWorkManager, recorder);

        await Should.ThrowAsync<OperationCanceledException>(
            () => runner.RunAsync(
                handler.JobKey,
                new JobRunContext { TriggerKind = JobTriggerKind.Manual },
                cancellation.Token));

        recorder.FinishedSuccess.ShouldBeNull();
        recorder.FinishCount.ShouldBe(0);
        recorder.InterruptCount.ShouldBe(1);
        recorder.InterruptedSummary.ShouldBe("任务执行已取消");
    }

    [Fact]
    public async Task RunForHangfire_取消必须携带Interrupted持久化确认与原始Token()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        using var handlerCancellation = new CancellationTokenSource();
        using var entryCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            handlerCancellation.Token);
        var handler = new CancelingHandler(
            "ScheduledJobRunner_HangfireCancellation",
            handlerCancellation);
        var recorder = new ProbeExecutionRecorder();
        var runner = CreateRunner(handler, unitOfWorkManager, recorder);

        var exception = await Should.ThrowAsync<ScheduledJobRecordedCancellationException>(
            () => runner.RunForHangfireAsync(
                handler.JobKey,
                new JobRunContext { TriggerKind = JobTriggerKind.Scheduled },
                entryCancellation.Token));

        exception.TerminalStateRecorded.ShouldBeTrue();
        exception.ExecutionId.ShouldNotBe(Guid.Empty);
        exception.ShouldBeAssignableTo<OperationCanceledException>();
        exception.CancellationToken.ShouldBe(handlerCancellation.Token);
        exception.CancellationToken.ShouldNotBe(entryCancellation.Token);
        exception.InnerException.ShouldBeNull();
        recorder.FinishCount.ShouldBe(0);
        recorder.InterruptCount.ShouldBe(1);
    }

    [Fact]
    public async Task Recorder取消必须持久化为Interrupted而非Failed()
    {
        var recorder = GetRequiredService<IScheduledJobExecutionRecorder>();
        var executionStart = await recorder.StartAsync(
            "ScheduledJobRecorder_Interrupted",
            "ProbeJob",
            "ExecuteAsync",
            null,
            JobTriggerKind.Manual);
        executionStart.Recorded.ShouldBeTrue();
        var executionId = executionStart.ExecutionId;

        (await recorder.InterruptAsync(executionId, "调用方取消")).ShouldBeTrue();

        await WithUnitOfWorkAsync(async () =>
        {
            var execution = await _executions.GetAsync(executionId);
            execution.Status.ShouldBe(JobExecutionStatus.Interrupted);
            execution.Summary.ShouldBe("调用方取消");
            execution.Error.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Recorder竞争跳过必须持久化为Skipped而非Succeeded()
    {
        var recorder = GetRequiredService<IScheduledJobExecutionRecorder>();
        var executionStart = await recorder.StartAsync(
            "ScheduledJobRecorder_Skipped",
            "ProbeJob",
            "ExecuteAsync",
            null,
            JobTriggerKind.Scheduled);
        executionStart.Recorded.ShouldBeTrue();

        (await recorder.SkipAsync(executionStart.ExecutionId, "未取得执行权")).ShouldBeTrue();

        await WithUnitOfWorkAsync(async () =>
        {
            var execution = await _executions.GetAsync(executionStart.ExecutionId);
            execution.Status.ShouldBe(JobExecutionStatus.Skipped);
            execution.Summary.ShouldBe("未取得执行权");
            execution.Error.ShouldBeNull();
        });
    }

    private ScheduledJobRunner CreateRunner(
        IScheduledJobHandler handler,
        IUnitOfWorkManager unitOfWorkManager,
        IScheduledJobExecutionRecorder recorder)
        => new(
            new SingleHandlerResolver(handler),
            recorder,
            _notification,
            _executions,
            GetRequiredService<IConfiguration>(),
            unitOfWorkManager,
            GetRequiredService<ILogger<ScheduledJobRunner>>(),
            GetRequiredService<IAsyncQueryableExecuter>());

    /// <summary>把单个探针处理器直接喂给 Runner 的解析器替身（不走容器，用例可自由构造探针）。</summary>
    private sealed class SingleHandlerResolver : IScheduledJobHandlerResolver
    {
        private readonly IScheduledJobHandler _handler;

        public SingleHandlerResolver(IScheduledJobHandler handler) => _handler = handler;

        public IScheduledJobHandler? Find(string jobKey)
            => _handler.JobKey == jobKey ? _handler : null;

        public IReadOnlyList<IScheduledJobHandler> ResolveAvailable() => new[] { _handler };
    }

    private sealed class UnitOfWorkProbeHandler : IScheduledJobHandler
    {
        private readonly IUnitOfWorkManager _unitOfWorkManager;

        public UnitOfWorkProbeHandler(
            string jobKey,
            IUnitOfWorkManager unitOfWorkManager,
            bool useUnitOfWork,
            bool isTransactional)
        {
            JobKey = jobKey;
            _unitOfWorkManager = unitOfWorkManager;
            UseUnitOfWork = useUnitOfWork;
            IsTransactional = isTransactional;
        }

        public string JobKey { get; }
        public bool UseUnitOfWork { get; }
        public bool IsTransactional { get; }
        public int CallCount { get; private set; }
        public IUnitOfWork? ObservedCurrent { get; private set; }
        public bool? ObservedIsTransactional { get; private set; }

        public Task<JobRunResult> RunAsync(
            JobRunContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ObservedCurrent = _unitOfWorkManager.Current;
            ObservedIsTransactional = ObservedCurrent?.Options.IsTransactional;
            return Task.FromResult(JobRunResult.Ok("probe"));
        }
    }

    private sealed class FixedResultHandler : IScheduledJobHandler
    {
        private readonly JobRunResult _result;

        public FixedResultHandler(string jobKey, JobRunResult result)
        {
            JobKey = jobKey;
            _result = result;
        }

        public string JobKey { get; }
        public bool UseUnitOfWork => false;
        public int CallCount { get; private set; }

        public Task<JobRunResult> RunAsync(
            JobRunContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class ProbeDispatchHandler : IScheduledJobHandler
    {
        public ProbeDispatchHandler(string jobKey)
        {
            JobKey = jobKey;
        }

        public string JobKey { get; }
        public int CallCount { get; private set; }
        public int FormalWriteCount { get; private set; }

        public Task<JobRunResult> RunAsync(
            JobRunContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (!context.IsDryRun)
            {
                FormalWriteCount++;
            }

            var output = new JobOutput
            {
                Title = "探针报表产出",
                Body = "探针产出正文",
                DistributionSource = NotificationSources.ScheduledJobs,
                Attachments = new List<NotificationAttachment>
                {
                    new()
                    {
                        FileName = "probe.csv",
                        Content = new byte[] { 1, 2, 3 },
                    },
                },
            };

            return Task.FromResult(JobRunResult.Ok(
                context.IsDryRun ? "演练完成" : "正式完成",
                output));
        }
    }

    private sealed class CancelingHandler : IScheduledJobHandler
    {
        private readonly CancellationTokenSource _cancellation;

        public CancelingHandler(string jobKey, CancellationTokenSource cancellation)
        {
            JobKey = jobKey;
            _cancellation = cancellation;
        }

        public string JobKey { get; }
        public bool UseUnitOfWork => false;

        public Task<JobRunResult> RunAsync(
            JobRunContext context,
            CancellationToken cancellationToken = default)
        {
            _cancellation.Cancel();
            throw new OperationCanceledException(_cancellation.Token);
        }
    }

    private sealed class ProbeExecutionRecorder : IScheduledJobExecutionRecorder
    {
        public bool StartPersistenceSucceeds { get; init; } = true;
        public bool TerminalPersistenceSucceeds { get; init; } = true;
        public bool? FinishedSuccess { get; private set; }
        public int FinishCount { get; private set; }
        public int InterruptCount { get; private set; }
        public int SkipCount { get; private set; }
        public string? InterruptedSummary { get; private set; }
        public string? SkippedSummary { get; private set; }

        public Task<ScheduledJobExecutionStartResult> StartAsync(
            string? jobKey,
            string jobType,
            string? jobMethod,
            string? hangfireJobId,
            JobTriggerKind triggerKind,
            bool isDryRun = false,
            string? argsJson = null)
            => Task.FromResult(new ScheduledJobExecutionStartResult(
                Guid.NewGuid(),
                StartPersistenceSucceeds));

        public Task<bool> FinishAsync(
            Guid executionId,
            bool success,
            string? summary,
            string? error)
        {
            FinishedSuccess = success;
            FinishCount++;
            return Task.FromResult(
                StartPersistenceSucceeds && TerminalPersistenceSucceeds);
        }

        public Task<bool> InterruptAsync(Guid executionId, string? summary = null)
        {
            InterruptCount++;
            InterruptedSummary = summary;
            return Task.FromResult(
                StartPersistenceSucceeds && TerminalPersistenceSucceeds);
        }

        public Task<bool> SkipAsync(Guid executionId, string? summary = null)
        {
            SkipCount++;
            SkippedSummary = summary;
            return Task.FromResult(
                StartPersistenceSucceeds && TerminalPersistenceSucceeds);
        }
    }

    /// <summary>反射拿到 <see cref="ScheduledJobRunner"/> 内部静态并发闸字典并同步占住指定 jobKey 的闸。
    /// 调用方负责在 finally 里 Release，否则该闸（进程静态、跨测试共享）会一直卡住后续同 jobKey 的用例。</summary>
    private static SemaphoreSlim AcquireGateForTest(string jobKey)
    {
        var field = typeof(ScheduledJobRunner).GetField("ConcurrencyGates", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ScheduledJobRunner.ConcurrencyGates 字段名已变，需同步维护本反射测试");
        var gates = (ConcurrentDictionary<string, SemaphoreSlim>)field.GetValue(null)!;
        var gate = gates.GetOrAdd(jobKey, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        return gate;
    }
}
