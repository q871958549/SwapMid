using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.Scheduling;

public enum ScheduledJobExecutionTerminalKind
{
    Succeeded,
    Failed,
    Interrupted,
    Skipped,
}

public enum ScheduledJobExecutionTerminalResult
{
    Applied,
    Missing,
    AlreadyTerminal,
    LostRace,
}

/// <summary>
/// 执行历史的条件收尾。只有仍是 Running 的行会被改成终态，避免过滤器收尾和启动僵尸清理互相覆盖。
/// </summary>
public interface IScheduledJobExecutionRepository : IRepository<ScheduledJobExecution, Guid>
{
    Task<ScheduledJobExecutionTerminalResult> TryTransitionFromRunningAsync(
        Guid id,
        ScheduledJobExecutionTerminalKind kind,
        DateTime finishedAt,
        string? summary,
        string? error,
        CancellationToken cancellationToken = default);
}
