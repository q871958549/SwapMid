using System;
using Shouldly;
using Xunit;

namespace SwapMid.DataIntegration;

public class PipelineExecutionTests
{
    [Fact]
    public void New_Execution_Should_Start_As_Running()
    {
        var execution = CreateExecution();

        execution.Status.ShouldBe(ExecutionStatus.Running);
        execution.FinishedAt.ShouldBeNull();
        execution.ProcessedRows.ShouldBeNull();
    }

    [Fact]
    public void Running_Should_Allow_All_Direct_Terminal_Transitions()
    {
        var unresolvedOnly = CreateExecution();
        Should.Throw<InvalidOperationException>(() =>
            unresolvedOnly.ResolveAsSucceeded(
                unresolvedOnly.StartedAt.AddMinutes(1)));
        Should.Throw<InvalidOperationException>(() =>
            unresolvedOnly.ResolveAsFailed(
                unresolvedOnly.StartedAt.AddMinutes(1)));

        var succeeded = CreateExecution();
        succeeded.Complete(succeeded.StartedAt.AddMinutes(1), 17);
        succeeded.Status.ShouldBe(ExecutionStatus.Succeeded);
        succeeded.ProcessedRows.ShouldBe(17);

        var failed = CreateExecution();
        failed.Fail(failed.StartedAt.AddMinutes(1));
        failed.Status.ShouldBe(ExecutionStatus.Failed);

        var cancelled = CreateExecution();
        cancelled.Cancel(cancelled.StartedAt.AddMinutes(1));
        cancelled.Status.ShouldBe(ExecutionStatus.Cancelled);

        var unknown = CreateExecution();
        unknown.MarkOutcomeUnknown(unknown.StartedAt.AddMinutes(1));
        unknown.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
    }

    [Fact]
    public void OutcomeUnknown_Should_Only_Resolve_To_Succeeded_Or_Failed()
    {
        var applied = CreateExecution();
        var appliedUnknownAt = applied.StartedAt.AddMinutes(1);
        applied.MarkOutcomeUnknown(appliedUnknownAt);
        applied.ResolveAsSucceeded(applied.StartedAt.AddMinutes(2), 23);
        applied.Status.ShouldBe(ExecutionStatus.Succeeded);
        applied.ProcessedRows.ShouldBe(23);
        applied.FinishedAt.ShouldBe(appliedUnknownAt);

        var notApplied = CreateExecution();
        var notAppliedUnknownAt = notApplied.StartedAt.AddMinutes(1);
        notApplied.MarkOutcomeUnknown(notAppliedUnknownAt);
        notApplied.ResolveAsFailed(notApplied.StartedAt.AddMinutes(2));
        notApplied.Status.ShouldBe(ExecutionStatus.Failed);
        notApplied.ProcessedRows.ShouldBeNull();
        notApplied.FinishedAt.ShouldBe(notAppliedUnknownAt);

        var cannotUseOrdinaryTerminalMethods = CreateExecution();
        cannotUseOrdinaryTerminalMethods.MarkOutcomeUnknown(
            cannotUseOrdinaryTerminalMethods.StartedAt.AddMinutes(1));
        Should.Throw<InvalidOperationException>(() =>
            cannotUseOrdinaryTerminalMethods.Complete(
                cannotUseOrdinaryTerminalMethods.StartedAt.AddMinutes(2)));
        Should.Throw<InvalidOperationException>(() =>
            cannotUseOrdinaryTerminalMethods.Fail(
                cannotUseOrdinaryTerminalMethods.StartedAt.AddMinutes(2)));
        Should.Throw<InvalidOperationException>(() =>
            cannotUseOrdinaryTerminalMethods.Cancel(
                cannotUseOrdinaryTerminalMethods.StartedAt.AddMinutes(2)));
        Should.Throw<InvalidOperationException>(() =>
            cannotUseOrdinaryTerminalMethods.MarkOutcomeUnknown(
                cannotUseOrdinaryTerminalMethods.StartedAt.AddMinutes(2)));
    }

    [Fact]
    public void Succeeded_Transitions_Should_Reject_Negative_Processed_Rows()
    {
        var direct = CreateExecution();
        Should.Throw<ArgumentOutOfRangeException>(() =>
            direct.Complete(direct.StartedAt.AddSeconds(1), -1));
        direct.Status.ShouldBe(ExecutionStatus.Running);

        var reconciled = CreateExecution();
        reconciled.MarkOutcomeUnknown(reconciled.StartedAt.AddSeconds(1));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            reconciled.ResolveAsSucceeded(
                reconciled.StartedAt.AddSeconds(2),
                -1));
        reconciled.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
    }

    [Fact]
    public void Confirmed_Terminal_State_Should_Be_Immutable()
    {
        var succeeded = CreateExecution();
        succeeded.Complete(succeeded.StartedAt.AddMinutes(1), 1);
        AssertCannotTransition(succeeded);

        var failed = CreateExecution();
        failed.Fail(failed.StartedAt.AddMinutes(1));
        AssertCannotTransition(failed);

        var cancelled = CreateExecution();
        cancelled.Cancel(cancelled.StartedAt.AddMinutes(1));
        AssertCannotTransition(cancelled);
    }

    [Fact]
    public void FinishedAt_Should_Never_Precede_StartedAt()
    {
        var execution = CreateExecution();

        execution.Complete(execution.StartedAt.AddMinutes(-1), 0);

        execution.FinishedAt.ShouldBe(execution.StartedAt);
    }

    private static PipelineExecution CreateExecution()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Pipeline",
            TriggerType.Manual);

    private static void AssertCannotTransition(PipelineExecution execution)
    {
        var finishedAt = execution.StartedAt.AddMinutes(2);
        Should.Throw<InvalidOperationException>(() =>
            execution.Complete(finishedAt));
        Should.Throw<InvalidOperationException>(() =>
            execution.Fail(finishedAt));
        Should.Throw<InvalidOperationException>(() =>
            execution.Cancel(finishedAt));
        Should.Throw<InvalidOperationException>(() =>
            execution.MarkOutcomeUnknown(finishedAt));
        Should.Throw<InvalidOperationException>(() =>
            execution.ResolveAsSucceeded(finishedAt));
        Should.Throw<InvalidOperationException>(() =>
            execution.ResolveAsFailed(finishedAt));
    }
}
