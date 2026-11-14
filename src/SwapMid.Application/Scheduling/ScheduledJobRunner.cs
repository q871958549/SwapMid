using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Notification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Uow;

namespace SwapMid.Scheduling;

/// <summary>
/// 统一执行管道：驱动 <see cref="IScheduledJobHandler"/>，统一 记执行历史 + 失败告警 + 产出物分发。
/// 材料超耗率是首个接入方；其余任务按需迁入。演练/正式的分发差异在此收敛。
/// </summary>
public partial class ScheduledJobRunner : ITransientDependency
{
    // 按 jobKey 互斥同任务并发执行的进程内轻量锁（静态：跨 Runner 实例共享，Runner 本身是 Transient）。
    // 覆盖「手动立即执行」撞「定时到点」的同进程竞态——材料超耗率这类「整月先删后插」任务并发写有数据风险。
    // 跨实例/跨进程的同一 recurring job 触发已由 Hangfire 存储层自身的分布式取任务机制保证互斥，不重复加分布式锁。
    // 旧轨（未迁 Runner 的 11 个任务）不在此覆盖范围——它们迁入 Runner（P2b）时自动获得该保护，不做旁路重复实现。
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ConcurrencyGates = new();


    // ⚠️ 这里刻意**不**注入 IEnumerable<IScheduledJobHandler>：解析该集合会一次性构造全部处理器，
    // 任一处理器依赖链断裂就会让集合解析整体抛异常，连坐掉所有 Runner 驱动任务；且该异常发生在
    // 下方 try 之外（甚至在 Runner 构造期），绕过「记执行历史 + 失败告警」，管控页毫无痕迹。
    // 改由 resolver 按 JobKey 逐个隔离构造，坏的只跳过自己。
    private readonly IScheduledJobHandlerResolver _handlerResolver;
    private readonly IScheduledJobExecutionRecorder _recorder;
    private readonly INotificationService _notificationService;
    private readonly IRepository<ScheduledJobExecution, Guid> _executionRepository;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly ILogger<ScheduledJobRunner> _logger;
    // Runner 不是 AppService（无 AsyncExecuter 基类属性），而 Application 层禁 using EF Core，
    // IAsyncQueryableExecuter 是 ABP 为此准备的正门。
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public ScheduledJobRunner(
        IScheduledJobHandlerResolver handlerResolver,
        IScheduledJobExecutionRecorder recorder,
        INotificationService notificationService,
        IRepository<ScheduledJobExecution, Guid> executionRepository,
        IConfiguration configuration,
        IUnitOfWorkManager uowManager,
        ILogger<ScheduledJobRunner> logger,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _handlerResolver = handlerResolver;
        _recorder = recorder;
        _notificationService = notificationService;
        _executionRepository = executionRepository;
        _configuration = configuration;
        _uowManager = uowManager;
        _logger = logger;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>按 key 执行任务：无论成败都记历史，失败告警，成功且有产出物则分发。返回执行结果供带参 / 演练端点回显。
    /// 同 jobKey 并发调用（手动撞定时）：后到者不等待、直接跳过并记一条历史，避免「整月先删后插」类任务并发写。</summary>
    public async Task<JobRunResult> RunAsync(
        string jobKey,
        JobRunContext context,
        CancellationToken cancellationToken = default)
    {
        var outcome = await RunCoreAsync(jobKey, context, cancellationToken);
        if (outcome.Cancellation != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(outcome.Cancellation)
                .Throw();
        }

        return outcome.Result;
    }

    private async Task<RunnerOutcome> RunCoreAsync(
        string jobKey,
        JobRunContext context,
        CancellationToken cancellationToken)
    {
        // 找不到 = 该 JobKey 未注册（多为 [ExposeServices] 漏标）或该处理器自身构造失败，
        // 两种情形 resolver 都已记日志说明真因（后者带完整异常链）。
        var handler = _handlerResolver.Find(jobKey)
            ?? throw new ArgumentException($"未找到 IScheduledJobHandler：{jobKey}");

        if (context.ArgsJson is
            { Length: > JobRunContext.MaxArgsJsonCharacters })
        {
            return await RecordRejectedInputAsync(
                jobKey,
                handler,
                context,
                $"任务参数过长：最多允许 {JobRunContext.MaxArgsJsonCharacters} 个字符");
        }

        // R2 并发加固（蓝图 §20.3）：闸 key 优先取互斥组名，多个 JobKey 共享同一组名时互相排斥
        // （如主数据全量与增量两条同步任务）；未设组名的 Handler 维持旧行为（闸 key = 自身 JobKey）。
        var gateKey = handler.MutexGroup ?? jobKey;
        var gate = ConcurrencyGates.GetOrAdd(gateKey, _ => new SemaphoreSlim(1, 1));
        bool enteredGate;
        try
        {
            enteredGate = await gate.WaitAsync(0, cancellationToken);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            var cancelledStart = await _recorder.StartAsync(
                jobKey,
                handler.GetType().Name,
                "RunAsync",
                null,
                context.TriggerKind,
                context.IsDryRun,
                context.ArgsJson);
            var cancellationRecorded = await _recorder.InterruptAsync(
                cancelledStart.ExecutionId,
                "任务在等待并发闸前已取消");
            return RunnerOutcome.Cancelled(
                cancellationRecorded,
                cancelledStart.ExecutionId,
                ex);
        }

        if (!enteredGate)
        {
            var skippedStart = await _recorder.StartAsync(
                jobKey, handler.GetType().Name, "RunAsync", null, context.TriggerKind, context.IsDryRun, context.ArgsJson);
            // 跳过历史文案带组名，便于运维一眼看出是撞了互斥组里的哪个同伴（而非误以为撞了自己）。
            var skipSummary = handler.MutexGroup != null
                ? $"已有同互斥组（{handler.MutexGroup}）任务执行中，本次跳过"
                : "已有同任务执行中，本次跳过";
            var skipRecorded = await _recorder.SkipAsync(
                skippedStart.ExecutionId,
                skipSummary);
            if (!skipRecorded)
            {
                _logger.LogWarning(
                    "定时任务 {JobKey} 命中并发闸后已跳过，但 Skipped 终态未确认落库；Hangfire 将标记记录异常且不自动重试",
                    jobKey);
            }

            _logger.LogInformation("定时任务 {JobKey} 命中并发闸 {GateKey}，本次跳过", jobKey, gateKey);
            return new RunnerOutcome(
                JobRunResult.Contended(skipSummary),
                skippedStart.ExecutionId,
                skipRecorded);
        }

        try
        {
            // 手动作业无 Hangfire recurring id，这里显式带 jobKey 记历史（补 P1a filter 对 EnqueueManual 的空缺）。
            var executionStart = await _recorder.StartAsync(
                jobKey, handler.GetType().Name, "RunAsync", null, context.TriggerKind, context.IsDryRun, context.ArgsJson);
            var executionId = executionStart.ExecutionId;

            JobRunResult? result = null;
            string? error = null;
            try
            {
                // 三态边界：默认事务 UoW；纯扫描可用非事务 UoW；需要在数据库边界外执行
                // 文件/网络副作用的协调器可完全禁用 Runner UoW，并自行管理短事务。
                if (handler.UseUnitOfWork)
                {
                    using var uow = _uowManager.Begin(
                        requiresNew: true,
                        isTransactional: handler.IsTransactional);
                    result = ValidateHandlerResult(
                        jobKey,
                        await handler.RunAsync(
                            context,
                            cancellationToken));
                    await uow.CompleteAsync(cancellationToken);
                }
                else
                {
                    if (_uowManager.Current != null)
                    {
                        throw new InvalidOperationException(
                            $"定时任务 {jobKey} 要求无 ambient UoW，但调用入口仍存在活动 UoW");
                    }

                    result = ValidateHandlerResult(
                        jobKey,
                        await handler.RunAsync(
                            context,
                            cancellationToken));
                }
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                // 取消不是业务失败，不走失败告警；但控制页/调用方必须能观察到取消，不能被吞成 Success=false。
                // 在穿透前以 Interrupted 收尾；若落库失败，Host filter 会基于 typed exception 再补一条 Interrupted。
                // 核心方法用普通结果把持久化确认带出；控制页外层传播原 OCE，Hangfire 专用外层改抛
                // 保留原 token 的 typed OCE marker，并显式维持 Faulted Task，避免 async method builder /
                // ABP adapter 归一、包装后丢失确认元数据，同时维持 worker shutdown 的重新入队语义。
                var cancellationRecorded = await _recorder.InterruptAsync(executionId, "任务执行已取消");
                return RunnerOutcome.Cancelled(cancellationRecorded, executionId, ex);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _logger.LogError(ex, "定时任务 {JobKey} 执行失败", jobKey);
            }

            if (error == null && result?.Status == JobExecutionStatus.Skipped)
            {
                var skipRecorded = await _recorder.SkipAsync(
                    executionId,
                    result.Summary);
                if (!skipRecorded)
                {
                    _logger.LogWarning(
                        "定时任务 {JobKey} 已因竞争跳过，但 Skipped 终态未确认落库；Hangfire 将标记记录异常且不自动重试",
                        jobKey);
                }

                return new RunnerOutcome(
                    result,
                    executionId,
                    skipRecorded);
            }

            var handlerFailed = error != null || result?.Status == JobExecutionStatus.Failed;

            // 分发在 FinishAsync 之前做：产出物「生成成功但没送达」对使用者而言就是失败，
            // 执行历史必须真实反映这一点——旧实现分发在 FinishAsync 之后，历史已写 Succeeded 无法再改，
            // SMTP 故障时管控页显示成功、也不告警，是唯一的正确性盲区（improvements.md #7）。
            // 三档语义：发送成功 / 配置性跳过（渠道未启用、无订阅接收人——记摘要不算失败，未配通知的环境
            // 不应常态告警）/ 真失败（渠道启用但发送异常——记 Failed + 告警）。
            var distribute = DistributeOutcome.NotAttempted;
            if (result?.Status == JobExecutionStatus.Succeeded
                && result.Output is { } output
                && (output.Attachments.Count > 0 || !string.IsNullOrWhiteSpace(output.Body)))
            {
                distribute = await DistributeAsync(output, context);
            }

            var failed = handlerFailed || distribute.Error != null;
            var finalError = error ?? distribute.Error;
            var finalSummary = distribute.Error != null
                ? $"{result?.Summary}；产出分发失败：{distribute.Error}"
                : distribute.SkipReason != null
                    ? $"{result?.Summary}；分发跳过：{distribute.SkipReason}"
                    : result?.Summary;
            var terminalStateRecorded = await _recorder.FinishAsync(
                executionId,
                !failed,
                finalSummary,
                finalError);

            if (failed)
            {
                // F5 告警抑制：演练失败不告警（操作者就在前端看着结果，发运维纯噪声）；
                // 正式失败按连续失败次数抑制——首次与每第 N 次告警，其余静默（防 30 分钟级任务
                // 在外部源宕机时按调度频率轰炸通知中心）。
                var failureNotificationHandled = context.IsDryRun;
                if (!context.IsDryRun && terminalStateRecorded)
                {
                    failureNotificationHandled = await TryNotifyFailureWithSuppressionAsync(
                        jobKey,
                        executionId,
                        finalError ?? finalSummary);
                }
                else if (!terminalStateRecorded)
                {
                    _logger.LogWarning(
                        "定时任务 {JobKey} 失败终态未确认落库，业务告警交由 Hangfire fallback 发送",
                        jobKey);
                }

                var failedResult = result is null
                    ? JobRunResult.Fail(finalError ?? "执行失败")
                    : new JobRunResult
                    {
                        Status = JobExecutionStatus.Failed,
                        Summary = finalSummary,
                        Output = result.Output,
                    };
                return new RunnerOutcome(
                    failedResult,
                    executionId,
                    terminalStateRecorded,
                    failureNotificationHandled);
            }

            // F5 恢复通知：本次正式执行成功且上一条正式终态是失败 → 发一条 Info，闭合「开始失败…已恢复」的告警区间。
            if (!context.IsDryRun && terminalStateRecorded)
            {
                await SafeNotifyRecoveryIfNeededAsync(jobKey, executionId);
            }

            var succeededResult = result is null || finalSummary == result.Summary
                ? result ?? JobRunResult.Ok(null)
                : new JobRunResult { Status = result.Status, Summary = finalSummary, Output = result.Output };
            if (!terminalStateRecorded)
            {
                _logger.LogWarning(
                    "定时任务 {JobKey} 已完成，但成功终态未确认落库；Hangfire 将标记记录异常且不自动重试",
                    jobKey);
            }

            return new RunnerOutcome(
                succeededResult,
                executionId,
                terminalStateRecorded);
        }
        finally
        {
            gate.Release();
        }
    }

    private static JobRunResult ValidateHandlerResult(
        string jobKey,
        JobRunResult? result)
    {
        if (result == null)
        {
            throw new InvalidOperationException(
                $"定时任务 {jobKey} 的处理器返回了 null 结果");
        }

        if (result.Status is not (JobExecutionStatus.Succeeded
            or JobExecutionStatus.Failed
            or JobExecutionStatus.Skipped))
        {
            throw new InvalidOperationException(
                $"定时任务 {jobKey} 的处理器返回了非终态状态：{result.Status}");
        }

        return result;
    }

    private readonly record struct RunnerOutcome(
        JobRunResult Result,
        Guid ExecutionId,
        bool TerminalStateRecorded,
        bool FailureNotificationHandled = false,
        OperationCanceledException? Cancellation = null)
    {
        public static RunnerOutcome Cancelled(
            bool terminalStateRecorded,
            Guid executionId,
            OperationCanceledException cancellation)
            => new(
                JobRunResult.Ok("任务执行已取消"),
                executionId,
                terminalStateRecorded,
                Cancellation: cancellation);
    }

    // 该任务最近的正式（非演练）终态记录，按开始时间倒序。排除 Running（并行读到的未完成条）与
    // Interrupted（进程重启的僵尸标记）与 Skipped（未取得执行权）都不是业务成败，
    // 不打断也不累计连续失败；因此 Failed → Skipped → Failed 仍按连续两次失败处理。
    private async Task<List<ScheduledJobExecution>> GetRecentFinalExecutionsAsync(string jobKey, int take)
    {
        using var uow = _uowManager.Begin(requiresNew: true);
        var queryable = await _executionRepository.GetQueryableAsync();
        var list = await _asyncExecuter.ToListAsync(queryable
            .Where(e => e.JobKey == jobKey && !e.IsDryRun
                && (e.Status == JobExecutionStatus.Succeeded || e.Status == JobExecutionStatus.Failed))
            .OrderByDescending(e => e.StartedAt)
            .ThenByDescending(e => e.Id)
            .Take(take));
        await uow.CompleteAsync();
        return list;
    }

}
