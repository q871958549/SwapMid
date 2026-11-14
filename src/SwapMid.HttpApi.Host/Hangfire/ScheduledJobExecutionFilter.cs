using System;
using System.Linq;
using SwapMid.Scheduling;
using Hangfire.Common;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SwapMid.Hangfire;

/// <summary>
/// Hangfire 全局 server 过滤器：给每个受管控作业执行留一条 <c>ScheduledJobExecution</c> 历史
/// （OnPerforming 起 Running、OnPerformed 收成败 / 耗时）。<c>JobKey</c> 取 Hangfire <c>RecurringJobId</c>
/// （= <c>ScheduledJobCatalog.Key</c>）；ABP 手工后台入口没有该参数时，从
/// <see cref="IRunnerDrivenJobArgs.JobKey"/> 恢复稳定 key，确保激活期失败仍能在管控页按任务归位。
/// 排除高频消息投递作业，避免历史表被淹。写库经独立 scope 的记录器、异常吞掉，绝不影响作业本身。
/// </summary>
public sealed class ScheduledJobExecutionFilter : JobFilterAttribute, IServerFilter
{
    private const string ExecutionStartKey = "swapmid.jobexec.start";

    // 高频 / 非受管控作业不留执行历史（如高频消息投递，非 Runner 驱动，按类型名排除）。
    // 经 ScheduledJobRunner 驱动的任务由 Runner 自记历史（带 jobKey + 业务摘要），其入口类实现 IRunnerDrivenJob
    // → 下方标记式跳过，迁新任务无需再改本名单（替代原先逐个加类型名的硬编码，易漏致 filter 与 Runner 双记）。
    private static readonly string[] ExcludedJobTypes = { "MessageDeliveryBackgroundJob" };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledJobExecutionFilter> _logger;

    public ScheduledJobExecutionFilter(IServiceScopeFactory scopeFactory, ILogger<ScheduledJobExecutionFilter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void OnPerforming(PerformingContext context)
    {
        try
        {
            var job = context.BackgroundJob.Job;
            // Runner 驱动的入口（标记 IRunnerDrivenJob）由 Runner 自记；高频降噪作业按类型名排除。二者都不在此记录。
            if (RunnerDrivenHangfireJobClassifier.IsRunnerDriven(job) || Array.IndexOf(ExcludedJobTypes, job.Type.Name) >= 0)
            {
                return;
            }

            var jobType = job.Type.Name;

            var recurringId = context.GetJobParameter<string>("RecurringJobId");
            var triggerKind = string.IsNullOrWhiteSpace(recurringId) ? JobTriggerKind.Manual : JobTriggerKind.Scheduled;

            using var scope = _scopeFactory.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IScheduledJobExecutionRecorder>();
            var executionStart = recorder
                .StartAsync(recurringId, jobType, job.Method.Name, context.BackgroundJob.Id, triggerKind)
                .GetAwaiter().GetResult();
            context.Items[ExecutionStartKey] = executionStart;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "定时任务执行历史 OnPerforming 记录失败");
        }
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.TryGetValue(ExecutionStartKey, out var obj)
            && obj is ScheduledJobExecutionStartResult executionStart)
        {
            var recurringId = context.GetJobParameter<string>("RecurringJobId");
            if (ScheduledJobRecordedFailureInspector.AllLeavesAreCancellation(context.Exception))
            {
                TryInterruptWithFallback(
                    context,
                    executionStart.ExecutionId,
                    recurringId,
                    "任务已取消");
                return;
            }

            TryFinishWithFallback(
                context,
                executionStart.ExecutionId,
                recurringId,
                context.Exception == null,
                summary: null,
                error: context.Exception == null
                    ? null
                    : ScheduledJobRecordedFailureInspector.DescribeLeaves(context.Exception));
            return;
        }

        // Runner 正常完成时已自行写入成功历史；它在 OnPerforming 阶段刻意不创建 Host 记录，
        // 因而没有 executionStart 是预期路径，不能误判成「未进入 Runner」再补一条失败历史。
        if (context.Exception == null)
        {
            return;
        }

        // Runner 已记完失败终态后主动抛给 Hangfire 的异常，不是「未进入 Runner」；
        // 即使被反射/框架/Aggregate 包装，也不能再补第二条历史。
        if (ScheduledJobRecordedFailureInspector.AllFailuresAreRecorded(context.Exception))
        {
            return;
        }

        var markers = ScheduledJobRecordedFailureInspector.GetTerminalStateMarkers(
            context.Exception);
        if (ScheduledJobRecordedFailureInspector.AllLeavesAreCancellation(context.Exception))
        {
            var cancellationMarkers = markers
                .OfType<ScheduledJobRecordedCancellationException>()
                .ToList();
            if (cancellationMarkers.Count == 0)
            {
                TryRecordNewInterruption(context);
            }
            else
            {
                foreach (var marker in cancellationMarkers.Where(x => !x.TerminalStateRecorded))
                {
                    TryInterruptWithFallback(
                        context,
                        marker.ExecutionId,
                        marker.JobKey,
                        "任务已取消，统一执行管道未确认原执行历史收尾");
                }
            }

            return;
        }

        if (ScheduledJobRecordedFailureInspector.AllLeavesAreUnconfirmedSuccessMarkers(
            context.Exception))
        {
            foreach (var marker in markers.OfType<ScheduledJobUnconfirmedSuccessException>())
            {
                TryFinishWithFallback(
                    context,
                    marker.ExecutionId,
                    marker.JobKey,
                    success: true,
                    summary: "任务已完成，统一执行管道未确认原成功终态，Host 已补偿",
                    error: null);
            }

            return;
        }

        if (ScheduledJobRecordedFailureInspector.AllLeavesAreUnconfirmedSkippedMarkers(
            context.Exception))
        {
            foreach (var marker in markers.OfType<ScheduledJobUnconfirmedSkippedException>())
            {
                TrySkipWithFallback(
                    context,
                    marker.ExecutionId,
                    marker.JobKey,
                    "任务因竞争跳过，统一执行管道未确认原 Skipped 终态，Host 已补偿");
            }

            return;
        }

        if (markers.Count > 0)
        {
            // 混合 Aggregate：补偿其中未确认的原记录，但不为已记录 marker 再新建第二条历史；
            // 其余真实异常由 Hangfire FailedState + 通用告警承载。
            foreach (var marker in markers.Where(x => !x.TerminalStateRecorded))
            {
                switch (marker)
                {
                    case ScheduledJobRecordedFailureException:
                        TryFinishWithFallback(
                            context,
                            marker.ExecutionId,
                            marker.JobKey,
                            success: false,
                            summary: "统一执行管道未确认原失败终态，Host 已补偿",
                            error: ScheduledJobRecordedFailureInspector.DescribeLeaves(
                                context.Exception));
                        break;
                    case ScheduledJobRecordedCancellationException:
                        TryInterruptWithFallback(
                            context,
                            marker.ExecutionId,
                            marker.JobKey,
                            "任务取消终态未确认，Host 已补偿");
                        break;
                    case ScheduledJobUnconfirmedSuccessException:
                        TryFinishWithFallback(
                            context,
                            marker.ExecutionId,
                            marker.JobKey,
                            success: true,
                            summary: "任务已完成，统一执行管道未确认原成功终态，Host 已补偿",
                            error: null);
                        break;
                    case ScheduledJobUnconfirmedSkippedException:
                        TrySkipWithFallback(
                            context,
                            marker.ExecutionId,
                            marker.JobKey,
                            "任务因竞争跳过，统一执行管道未确认原 Skipped 终态，Host 已补偿");
                        break;
                }
            }

            return;
        }

        TryRecordPreRunnerFailure(context);
    }

    /// <summary>
    /// 补记「还没进到 Runner 就失败」的执行历史。
    ///
    /// Runner 驱动的作业在 <see cref="OnPerforming"/> 被刻意跳过（由 Runner 自记带业务摘要的历史，
    /// 防双记）。但若作业在**进入 Runner 之前**就炸——依赖注入解析失败、Runner 或其依赖构造异常、
    /// 作业激活失败——Runner 压根没运行，于是这次失败在执行历史里零痕迹：运维只收到一条 Hangfire
    /// 告警说「某任务失败了」，翻开管控页却什么都看不到，只能去连生产 Hangfire Dashboard 找堆栈。
    /// 这里补一条 Failed 历史（带完整异常链），让「为什么挂」在管控页就能看到。
    /// </summary>
    private void TryRecordPreRunnerFailure(PerformedContext context)
    {
        if (context.Exception == null)
        {
            return;
        }

        try
        {
            var job = context.BackgroundJob.Job;
            // 只兜 Runner 驱动的作业：其余作业 OnPerforming 已开好记录，走上面的正常收尾路径。
            if (!RunnerDrivenHangfireJobClassifier.IsRunnerDriven(job))
            {
                return;
            }

            var recurringId = context.GetJobParameter<string>("RecurringJobId");
            var jobKey = ResolveJobKey(job, recurringId);
            var triggerKind = string.IsNullOrWhiteSpace(recurringId) ? JobTriggerKind.Manual : JobTriggerKind.Scheduled;

            using var scope = _scopeFactory.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IScheduledJobExecutionRecorder>();
            var executionStart = recorder
                .StartAsync(jobKey, job.Type.Name, job.Method.Name, context.BackgroundJob.Id, triggerKind)
                .GetAwaiter().GetResult();
            TryFinishWithFallback(
                context,
                executionStart.ExecutionId,
                jobKey,
                success: false,
                summary: "任务未进入统一执行管道即失败（依赖注入 / 作业激活阶段异常）",
                error: ScheduledJobRecordedFailureInspector.DescribeLeaves(context.Exception));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "定时任务激活期失败的执行历史补记失败");
        }
    }

    /// <summary>
    /// 取消不属于业务失败。Runner 已进入但 Interrupt 落库失败，或进入 Runner 前即取消时，
    /// 补一条 Interrupted 历史；绝不把取消误记为 Failed。
    /// </summary>
    private void TryRecordNewInterruption(PerformedContext context)
    {
        try
        {
            var job = context.BackgroundJob.Job;
            if (!RunnerDrivenHangfireJobClassifier.IsRunnerDriven(job))
            {
                return;
            }

            var recurringId = context.GetJobParameter<string>("RecurringJobId");
            var jobKey = ResolveJobKey(job, recurringId);
            var triggerKind = string.IsNullOrWhiteSpace(recurringId)
                ? JobTriggerKind.Manual
                : JobTriggerKind.Scheduled;

            using var scope = _scopeFactory.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IScheduledJobExecutionRecorder>();
            var executionStart = recorder
                .StartAsync(
                    jobKey,
                    job.Type.Name,
                    job.Method.Name,
                    context.BackgroundJob.Id,
                    triggerKind)
                .GetAwaiter()
                .GetResult();
            TryInterruptWithFallback(
                context,
                executionStart.ExecutionId,
                jobKey,
                "任务已取消，统一执行管道未确认原执行历史收尾");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "定时任务取消的执行历史补记失败");
        }
    }

    private void TryFinishWithFallback(
        PerformedContext context,
        Guid executionId,
        string? jobKey,
        bool success,
        string? summary,
        string? error)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IScheduledJobExecutionRecorder>();
            var recorded = recorder
                .FinishAsync(executionId, success, summary, error)
                .GetAwaiter()
                .GetResult();
            if (recorded)
            {
                return;
            }

            var fallbackStart = StartFallback(context, recorder, jobKey);
            var fallbackRecorded = recorder
                .FinishAsync(
                    fallbackStart.ExecutionId,
                    success,
                    summary,
                    error)
                .GetAwaiter()
                .GetResult();
            if (!fallbackRecorded)
            {
                _logger.LogError(
                    "定时任务终态补偿未确认落库 jobKey={JobKey} originalExecutionId={ExecutionId} fallbackExecutionId={FallbackId}",
                    jobKey,
                    executionId,
                    fallbackStart.ExecutionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "定时任务终态补偿异常 jobKey={JobKey} executionId={ExecutionId}",
                jobKey,
                executionId);
        }
    }

    private void TryInterruptWithFallback(
        PerformedContext context,
        Guid executionId,
        string? jobKey,
        string summary)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IScheduledJobExecutionRecorder>();
            var recorded = recorder
                .InterruptAsync(executionId, summary)
                .GetAwaiter()
                .GetResult();
            if (recorded)
            {
                return;
            }

            var fallbackStart = StartFallback(context, recorder, jobKey);
            var fallbackRecorded = recorder
                .InterruptAsync(fallbackStart.ExecutionId, summary)
                .GetAwaiter()
                .GetResult();
            if (!fallbackRecorded)
            {
                _logger.LogError(
                    "定时任务取消终态补偿未确认落库 jobKey={JobKey} originalExecutionId={ExecutionId} fallbackExecutionId={FallbackId}",
                    jobKey,
                    executionId,
                    fallbackStart.ExecutionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "定时任务取消终态补偿异常 jobKey={JobKey} executionId={ExecutionId}",
                jobKey,
                executionId);
        }
    }

    private void TrySkipWithFallback(
        PerformedContext context,
        Guid executionId,
        string? jobKey,
        string summary)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IScheduledJobExecutionRecorder>();
            var recorded = recorder
                .SkipAsync(executionId, summary)
                .GetAwaiter()
                .GetResult();
            if (recorded)
            {
                return;
            }

            var fallbackStart = StartFallback(context, recorder, jobKey);
            var fallbackRecorded = recorder
                .SkipAsync(fallbackStart.ExecutionId, summary)
                .GetAwaiter()
                .GetResult();
            if (!fallbackRecorded)
            {
                _logger.LogError(
                    "定时任务跳过终态补偿未确认落库 jobKey={JobKey} originalExecutionId={ExecutionId} fallbackExecutionId={FallbackId}",
                    jobKey,
                    executionId,
                    fallbackStart.ExecutionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "定时任务跳过终态补偿异常 jobKey={JobKey} executionId={ExecutionId}",
                jobKey,
                executionId);
        }
    }

    private static ScheduledJobExecutionStartResult StartFallback(
        PerformedContext context,
        IScheduledJobExecutionRecorder recorder,
        string? jobKey)
    {
        var job = context.BackgroundJob.Job;
        var recurringId = context.GetJobParameter<string>("RecurringJobId");
        var triggerKind = string.IsNullOrWhiteSpace(recurringId)
            ? JobTriggerKind.Manual
            : JobTriggerKind.Scheduled;
        return recorder
            .StartAsync(
                jobKey,
                job.Type.Name,
                job.Method.Name,
                context.BackgroundJob.Id,
                triggerKind)
            .GetAwaiter()
            .GetResult();
    }

    private static string? ResolveJobKey(Job job, string? recurringId)
    {
        if (!string.IsNullOrWhiteSpace(recurringId))
        {
            return recurringId;
        }

        return job.Args
            .OfType<IRunnerDrivenJobArgs>()
            .Select(args => args.JobKey)
            .FirstOrDefault(jobKey => !string.IsNullOrWhiteSpace(jobKey));
    }
}
