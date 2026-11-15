using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;
/// <summary>仅供可控测试锁暴露租约丢失信号。</summary>
internal interface IDataPipelineLeaseLossAwareHandle
{
    CancellationToken HandleLostToken { get; }
}

public sealed partial class DataPipelineExecutionCoordinator
{

    public const string DistributedLockPrefix =
        "swapmid:data-integration:pipeline:";
    private const int StateCommitConfirmationTimeoutSeconds = 30;

    private async Task<IAbpDistributedLockHandle?> TryAcquireDistributedLockAsync(
        Guid pipelineId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _distributedLock.TryAcquireAsync(
                DistributedLockPrefix + pipelineId.ToString("N"),
                TimeSpan.Zero,
                cancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception lockException)
        {
            RecordAdmission("coordination_failure");
            _logger.LogError(
                "ETL Redis 执行锁获取失败 pipelineId={PipelineId} exceptionType={ExceptionType}",
                pipelineId,
                lockException.GetType().FullName ?? lockException.GetType().Name);
            throw CoordinationUnavailable();
        }
    }

    private async Task<Preparation> PrepareAsync(
        Guid pipelineId,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(
            requiresNew: true,
            isTransactional: false);
        var existing = await _executionRepository.FindAsync(
            executionId,
            includeDetails: false,
            cancellationToken: cancellationToken);
        if (existing != null && existing.PipelineId != pipelineId)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionInputInvalid);
        }

        if (existing is { Status: not ExecutionStatus.Running })
        {
            await unitOfWork.CompleteAsync(cancellationToken);
            return new Preparation(existing, Identity: null);
        }

        var pipeline = await _pipelineRepository.GetAsync(
            pipelineId,
            includeDetails: false,
            cancellationToken: cancellationToken);
        ValidatePipeline(pipeline);
        var source = await _sourceRepository.GetAsync(
            pipeline.SourceId,
            includeDetails: false,
            cancellationToken: cancellationToken);
        var target = await _sourceRepository.GetAsync(
            pipeline.TargetId!.Value,
            includeDetails: false,
            cancellationToken: cancellationToken);
        var identity = BuildExecutionIdentity(pipeline, source, target);
        await unitOfWork.CompleteAsync(cancellationToken);
        return new Preparation(existing, identity);
    }


    private async Task<PipelineExecution> FinalizeAsync(
        Guid executionId,
        TerminalKind kind,
        long? processedRows,
        PipelineExecutionFailurePolicy.FailureDescriptor? failure,
        EtlTargetReceiptEvidence? receiptEvidence = null)
    {
        try
        {
            var execution = await FinalizeCoreAsync(
                executionId,
                kind,
                processedRows,
                failure,
                receiptEvidence);
            await UpdateProjectionNoThrowAsync(execution);
            RecordResult(execution.Status);
            return execution;
        }
        catch (PipelineExecutionStateCommitUnknownException)
        {
            RecordCoordinationEvent("state_commit_outcome_unknown");
            // 只重试平台终态写入，绝不再次调用外部 ETL。
            try
            {
                var execution = await FinalizeCoreAsync(
                    executionId,
                    kind,
                    processedRows,
                    failure,
                    receiptEvidence);
                await UpdateProjectionNoThrowAsync(execution);
                RecordResult(execution.Status);
                return execution;
            }
            catch (PipelineExecutionStateCommitUnknownException)
            {
                _logger.LogCritical(
                    "ETL 平台终态连续两次无法确认 executionId={ExecutionId}；禁止重跑目标任务",
                    executionId);
                throw CoordinationUnavailable();
            }
        }
    }

    private async Task<PipelineExecution> FinalizeCoreAsync(
        Guid executionId,
        TerminalKind kind,
        long? processedRows,
        PipelineExecutionFailurePolicy.FailureDescriptor? safeFailure,
        EtlTargetReceiptEvidence? receiptEvidence)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(
            requiresNew: true,
            isTransactional: true);
        _terminalStoreTopologyGuard.EnsureSafeScope(
            PipelineTerminalStoreTopologyPhase.TerminalFinalize);
        var execution = await _executionRepository.GetAsync(
            executionId,
            includeDetails: false,
            cancellationToken: CancellationToken.None);

        if (HasRunningRecoveryMarker(execution))
        {
            RecordCoordinationEvent(
                "running_recovery_late_finalize_blocked");
            _logger.LogCritical(
                "ETL 已受控恢复的执行收到晚到终态结算，已失败关闭 executionId={ExecutionId} terminalKind={TerminalKind}",
                executionId,
                kind);
            throw CoordinationUnavailable();
        }

        var receiptEvidenceChanged = StoreReceiptEvidence(
            execution,
            receiptEvidence);
        PipelineExecutionFailurePolicy.FailureDescriptor? failure = null;
        var outcomeUnknown = false;
        var transitionRequired = !IsDesiredState(execution.Status, kind);
        if (transitionRequired)
        {
            switch (kind)
            {
                case TerminalKind.Succeeded:
                    if (execution.Status == ExecutionStatus.OutcomeUnknown)
                    {
                        execution.ResolveAsSucceeded(
                            DateTime.UtcNow,
                            processedRows);
                    }
                    else
                    {
                        execution.Complete(DateTime.UtcNow, processedRows);
                    }
                    break;
                case TerminalKind.Failed:
                    failure = _failurePolicy.Record(
                        execution,
                        RequireFailure(safeFailure, kind),
                        DateTime.UtcNow);
                    break;
                case TerminalKind.CoordinationFailed:
                    failure = _failurePolicy.RecordCoordinationFailure(
                        execution,
                        RequireFailure(safeFailure, kind),
                        DateTime.UtcNow);
                    break;
                case TerminalKind.Cancelled:
                    execution.Cancel(DateTime.UtcNow);
                    break;
                case TerminalKind.OutcomeUnknown:
                    failure = _failurePolicy.RecordOutcomeUnknown(
                        execution,
                        RequireFailure(safeFailure, kind),
                        DateTime.UtcNow);
                    outcomeUnknown = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }

        }

        if (receiptEvidenceChanged || transitionRequired)
        {
            await _executionRepository.UpdateAsync(
                execution,
                autoSave: false,
                CancellationToken.None);
        }

        if (failure != null)
        {
            await _notificationScheduler.ScheduleAsync(
                execution.PipelineId,
                execution.Id,
                failure,
                outcomeUnknown);
        }

        if (transitionRequired && _terminalEventScheduler != null)
        {
            await _terminalEventScheduler.ScheduleTerminalEventAsync(
                execution,
                failure);
        }

        await CompleteUnitOfWorkAsync(unitOfWork, "terminal_state");
        return execution;
    }

    private async Task CompleteUnitOfWorkAsync(
        IUnitOfWork unitOfWork,
        string phase)
    {
        try
        {
            using var commitConfirmation = new CancellationTokenSource(
                TimeSpan.FromSeconds(
                    StateCommitConfirmationTimeoutSeconds));
            await unitOfWork.CompleteAsync(commitConfirmation.Token);
        }
        catch (Exception commitException)
        {
            if (unitOfWork.IsCompleted)
            {
                _logger.LogError(
                    "ETL 平台状态已提交但后置回调失败 phase={Phase} exceptionType={ExceptionType}",
                    phase,
                    commitException.GetType().FullName ??
                    commitException.GetType().Name);
                return;
            }

            _logger.LogCritical(
                "ETL 平台状态提交结果无法确认 phase={Phase} exceptionType={ExceptionType}",
                phase,
                commitException.GetType().FullName ??
                commitException.GetType().Name);
            throw new PipelineExecutionStateCommitUnknownException();
        }
    }

    private async Task UpdateProjectionNoThrowAsync(
        PipelineExecution execution)
    {
        try
        {
            using var unitOfWork = _unitOfWorkManager.Begin(
                requiresNew: true,
                isTransactional: true);
            var pipeline = await _pipelineRepository.FindAsync(
                execution.PipelineId,
                includeDetails: false,
                cancellationToken: CancellationToken.None);
            if (pipeline == null)
            {
                await CompleteUnitOfWorkAsync(
                    unitOfWork,
                    "projection_missing_pipeline");
                return;
            }

            pipeline.RecordExecution(execution.StartedAt, execution.Status);
            await _pipelineRepository.UpdateAsync(
                pipeline,
                autoSave: false,
                CancellationToken.None);
            await CompleteUnitOfWorkAsync(unitOfWork, "projection_state");
        }
        catch (Exception projectionException)
        {
            RecordCoordinationEvent("projection_write_failure");
            _logger.LogError(
                "ETL 最近执行投影更新失败 executionId={ExecutionId} exceptionType={ExceptionType}",
                execution.Id,
                projectionException.GetType().FullName ??
                projectionException.GetType().Name);
        }
    }

    private sealed record Preparation(
        PipelineExecution? ExistingExecution,
        ExecutionIdentity? Identity);


    private sealed record Claim(
        PipelineExecution Execution,
        DataSource Source,
        DataSource Target,
        string? TransformConfig,
        EtlExecutionContext? ExecutionContext,
        bool ShouldExecute,
        bool OutcomeUnresolved)
    {
        public static Claim Start(
            PipelineExecution execution,
            DataSource source,
            DataSource target,
            string? transformConfig,
            EtlExecutionContext executionContext)
            => new(
                execution,
                source,
                target,
                transformConfig,
                executionContext,
                ShouldExecute: true,
                OutcomeUnresolved: false);

        public static Claim Replay(PipelineExecution execution)
            => new(
                execution,
                null!,
                null!,
                null,
                null,
                ShouldExecute: false,
                OutcomeUnresolved: false);

        public static Claim Blocked(PipelineExecution execution)
            => new(
                execution,
                null!,
                null!,
                null,
                null,
                ShouldExecute: false,
                OutcomeUnresolved: true);
    }

    private enum TerminalKind
    {
        Succeeded,
        Failed,
        CoordinationFailed,
        Cancelled,
        OutcomeUnknown,
    }
}

internal sealed class PipelineExecutionStateCommitUnknownException : Exception
{
}
