using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Scheduling;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore.Scheduling;

public class EfCoreScheduledJobExecutionRepository
    : EfCoreRepository<SwapMidDbContext, ScheduledJobExecution, Guid>,
        IScheduledJobExecutionRepository
{
    public EfCoreScheduledJobExecutionRepository(
        IDbContextProvider<SwapMidDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<ScheduledJobExecutionTerminalResult> TryTransitionFromRunningAsync(
        Guid id,
        ScheduledJobExecutionTerminalKind kind,
        DateTime finishedAt,
        string? summary,
        string? error,
        CancellationToken cancellationToken = default)
    {
        var set = await GetDbSetAsync();
        var token = GetCancellationToken(cancellationToken);
        var current = await set.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Status, x.StartedAt })
            .FirstOrDefaultAsync(token);
        if (current == null)
        {
            return ScheduledJobExecutionTerminalResult.Missing;
        }

        if (current.Status != JobExecutionStatus.Running)
        {
            return ScheduledJobExecutionTerminalResult.AlreadyTerminal;
        }

        var draft = new ScheduledJobExecution(
            id,
            null,
            "transition",
            null,
            null,
            JobTriggerKind.Manual,
            current.StartedAt);
        switch (kind)
        {
            case ScheduledJobExecutionTerminalKind.Succeeded:
                draft.MarkFinished(finishedAt, true, summary, error);
                break;
            case ScheduledJobExecutionTerminalKind.Failed:
                draft.MarkFinished(finishedAt, false, summary, error);
                break;
            case ScheduledJobExecutionTerminalKind.Interrupted:
                draft.MarkInterrupted(finishedAt, summary);
                break;
            case ScheduledJobExecutionTerminalKind.Skipped:
                draft.MarkSkipped(finishedAt, summary);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        try
        {
            var affected = await set
                .Where(x => x.Id == id && x.Status == JobExecutionStatus.Running)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, draft.Status)
                        .SetProperty(x => x.FinishedAt, draft.FinishedAt)
                        .SetProperty(x => x.DurationMs, draft.DurationMs)
                        .SetProperty(x => x.Summary, draft.Summary)
                        .SetProperty(x => x.Error, draft.Error),
                    token);
            return affected == 1
                ? ScheduledJobExecutionTerminalResult.Applied
                : ScheduledJobExecutionTerminalResult.LostRace;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ScheduledJobExecutionTerminalResult.LostRace;
        }
    }
}
