using System;
using System.Linq;
using System.Threading;
using SwapMid.Monitoring;
using Volo.Abp;
using Volo.Abp.DistributedLocking;

namespace SwapMid.DataIntegration;

public sealed partial class DataPipelineExecutionCoordinator
{
    private void EnsureNoAmbientUnitOfWork()
    {
        if (_unitOfWorkManager.Current != null)
        {
            throw new InvalidOperationException(
                "ETL 执行不得在 ambient UoW 中调用；必须由协调器管理短事务");
        }
    }

    private static void ValidatePipeline(DataPipeline pipeline)
    {
        if (pipeline.Status != PipelineStatus.Active)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineNotActive);
        }

        if (pipeline.TargetId == null)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineTargetNotConfigured);
        }
    }

    private static bool IsSamePipelineOrTarget(
        PipelineExecution execution,
        Guid pipelineId,
        System.Collections.Generic.IReadOnlyCollection<string>
            targetResources)
        => execution.PipelineId == pipelineId ||
           GetTargetResources(execution).Any(targetResources.Contains);

    private static bool HasMissingTargetResource(PipelineExecution execution)
        => GetTargetResources(execution).Count == 0;

    private static System.Collections.Generic.IReadOnlyList<string>
        GetTargetResources(PipelineExecution execution)
    {
        var legacy = execution.TargetResourceHash ??
            ReadStringProperty(execution, TargetResourcePropertyName);
        return new[] { legacy, execution.VerifiedTargetResourceHash }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static CancellationToken GetHandleLostToken(
        IAbpDistributedLockHandle lockHandle)
    {
        var token = lockHandle switch
        {
            MedallionAbpDistributedLockHandle medallionHandle =>
                medallionHandle.Handle.HandleLostToken,
            IDataPipelineLeaseLossAwareHandle testHandle =>
                testHandle.HandleLostToken,
            _ => CancellationToken.None,
        };
        if (!token.CanBeCanceled)
        {
            throw CoordinationUnavailable();
        }

        token.ThrowIfCancellationRequested();
        return token;
    }

    private static BusinessException CoordinationUnavailable()
        => new(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);

    private static bool IsDesiredState(
        ExecutionStatus status,
        TerminalKind kind)
        => kind switch
        {
            TerminalKind.Succeeded => status == ExecutionStatus.Succeeded,
            TerminalKind.Failed or TerminalKind.CoordinationFailed =>
                status is ExecutionStatus.Failed or ExecutionStatus.OutcomeUnknown,
            TerminalKind.Cancelled =>
                status is ExecutionStatus.Cancelled or ExecutionStatus.OutcomeUnknown,
            TerminalKind.OutcomeUnknown =>
                status == ExecutionStatus.OutcomeUnknown,
            _ => false,
        };

    private static PipelineExecutionFailurePolicy.FailureDescriptor
        RequireFailure(
            PipelineExecutionFailurePolicy.FailureDescriptor? failure,
            TerminalKind kind)
        => failure ?? throw new InvalidOperationException(
            $"ETL terminal kind {kind} requires a safe failure descriptor.");

    private static void RecordAdmission(string outcome)
        => PlatformMetrics.EtlExecutionAdmissionTotal.WithLabels(outcome).Inc();

    private static void RecordCoordinationEvent(string outcome)
        => PlatformMetrics.EtlExecutionCoordinationEventsTotal
            .WithLabels(outcome)
            .Inc();

    private static void RecordResult(ExecutionStatus status)
        => PlatformMetrics.EtlExecutionResultsTotal
            .WithLabels(status switch
            {
                ExecutionStatus.Succeeded => "succeeded",
                ExecutionStatus.Failed => "failed",
                ExecutionStatus.Cancelled => "cancelled",
                ExecutionStatus.OutcomeUnknown => "outcome_unknown",
                _ => "unexpected",
            })
            .Inc();
}
