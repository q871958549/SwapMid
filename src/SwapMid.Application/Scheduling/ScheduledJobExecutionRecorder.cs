using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace SwapMid.Scheduling;

/// <summary>
/// 执行历史记录器实现：由 Host 层 Hangfire 过滤器在作业前后调用。
/// 每次各起独立 UoW（过滤器线程无 ambient UoW），写入失败只记日志、绝不影响作业本身。
/// </summary>
public class ScheduledJobExecutionRecorder : IScheduledJobExecutionRecorder, ITransientDependency
{
    private const int TerminalAttempts = 3;

    private readonly IScheduledJobExecutionRepository _repository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IClock _clock;
    private readonly ILogger<ScheduledJobExecutionRecorder> _logger;

    public ScheduledJobExecutionRecorder(
        IScheduledJobExecutionRepository repository,
        IGuidGenerator guidGenerator,
        IUnitOfWorkManager uowManager,
        IClock clock,
        ILogger<ScheduledJobExecutionRecorder> logger)
    {
        _repository = repository;
        _guidGenerator = guidGenerator;
        _uowManager = uowManager;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ScheduledJobExecutionStartResult> StartAsync(
        string? jobKey,
        string jobType,
        string? jobMethod,
        string? hangfireJobId,
        JobTriggerKind triggerKind,
        bool isDryRun = false,
        string? argsJson = null)
    {
        var id = _guidGenerator.Create();
        try
        {
            using var uow = _uowManager.Begin(requiresNew: true, isTransactional: true);
            await _repository.InsertAsync(
                new ScheduledJobExecution(id, jobKey, jobType, jobMethod, hangfireJobId, triggerKind, _clock.Now, isDryRun, argsJson),
                autoSave: true);
            await uow.CompleteAsync();
            return new ScheduledJobExecutionStartResult(id, Recorded: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "定时任务执行历史 Start 写入失败 jobType={JobType}", jobType);
            return new ScheduledJobExecutionStartResult(id, Recorded: false);
        }
    }

    public Task<bool> FinishAsync(Guid executionId, bool success, string? summary, string? error)
        => TransitionAsync(
            executionId,
            success ? ScheduledJobExecutionTerminalKind.Succeeded : ScheduledJobExecutionTerminalKind.Failed,
            summary,
            error,
            "Finish");

    public Task<bool> InterruptAsync(Guid executionId, string? summary = null)
        => TransitionAsync(executionId, ScheduledJobExecutionTerminalKind.Interrupted, summary, null, "Interrupt");

    public Task<bool> SkipAsync(Guid executionId, string? summary = null)
        => TransitionAsync(executionId, ScheduledJobExecutionTerminalKind.Skipped, summary, null, "Skip");

    private async Task<bool> TransitionAsync(
        Guid executionId,
        ScheduledJobExecutionTerminalKind kind,
        string? summary,
        string? error,
        string operation)
    {
        for (var attempt = 1; attempt <= TerminalAttempts; attempt++)
        {
            try
            {
                using var uow = _uowManager.Begin(requiresNew: true, isTransactional: true);
                var result = await _repository.TryTransitionFromRunningAsync(
                    executionId,
                    kind,
                    _clock.Now,
                    summary,
                    error);
                await uow.CompleteAsync();
                switch (result)
                {
                    case ScheduledJobExecutionTerminalResult.Applied:
                        return true;
                    case ScheduledJobExecutionTerminalResult.AlreadyTerminal:
                        _logger.LogInformation(
                            "定时任务执行历史 {Operation} 发现记录已是终态，保留先写入的结果 executionId={Id}",
                            operation,
                            executionId);
                        return true;
                    case ScheduledJobExecutionTerminalResult.Missing:
                        _logger.LogWarning(
                            "定时任务执行历史 {Operation} 未找到记录 executionId={Id}",
                            operation,
                            executionId);
                        return false;
                    case ScheduledJobExecutionTerminalResult.LostRace:
                        _logger.LogInformation(
                            "定时任务执行历史 {Operation} 并发冲突，准备重试 {Attempt}/{Max} executionId={Id}",
                            operation,
                            attempt,
                            TerminalAttempts,
                            executionId);
                        break;
                }
            }
            catch (AbpDbConcurrencyException ex) when (attempt < TerminalAttempts)
            {
                _logger.LogInformation(
                    ex,
                    "定时任务执行历史 {Operation} 并发异常，准备重试 {Attempt}/{Max} executionId={Id}",
                    operation,
                    attempt,
                    TerminalAttempts,
                    executionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "定时任务执行历史 {Operation} 写入失败 executionId={Id}", operation, executionId);
                return false;
            }
        }

        _logger.LogWarning(
            "定时任务执行历史 {Operation} 重试后仍未写入 executionId={Id}",
            operation,
            executionId);
        return false;
    }
}
