using System;
using System.Linq;
using System.Text.Json;
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


public sealed partial class DataPipelineExecutionCoordinator
{

    public const string ResolutionPropertyName =
        "PipelineExecution.Resolution";
    public const string ResolvedAtPropertyName =
        "PipelineExecution.ResolvedAt";
    public const string ResolvedByPropertyName =
        "PipelineExecution.ResolvedBy";
    public const string VerificationNotePropertyName =
        "PipelineExecution.VerificationNote";

    /// <summary>
    /// 人工核对目标库后收敛 OutcomeUnknown。对账与正常执行共用两层执行权，防止核对期间
    /// 有新任务进入；重复提交相同结论只返回既有终态，不重复变更。
    /// </summary>
    public async Task<PipelineExecution> ResolveOutcomeAsync(
        Guid executionId,
        ResolvePipelineExecutionOutcomeInput input,
        Guid resolverId,
        CancellationToken cancellationToken = default)
    {
        EnsureNoAmbientUnitOfWork();
        var resolution = RequireResolution(input);
        var verificationNote = NormalizeVerificationNote(input, resolution);
        using var hostScope = _currentTenant.Change(null);
        var preparation = await PrepareResolutionAsync(
            executionId,
            resolution,
            requiredTopologyPhase: null,
            loadReceiptTarget: false,
            cancellationToken);
        if (preparation.ExistingResolution)
        {
            return preparation.Execution;
        }
        if (preparation.ReceiptContext != null &&
            IsLegacyRedactedVerificationNote(verificationNote))
        {
            // receipt-v1 是本轮新增协议，不存在需要兼容的旧客户端。它的破玻璃
            // 裁决仍必须引用真实结构化证据；兼容占位只服务既有 manual-v0。
            throw ResolutionInvalid();
        }

        var topologyPhase = GetResolutionTopologyPhase(resolution);
        var terminalStorePin = await _terminalStoreTopologyGuard
            .CaptureSafeAsync(topologyPhase, cancellationToken);
        var terminalStoreScope = _terminalStoreTopologyGuard.EnterSafeScope(
            terminalStorePin,
            topologyPhase);

        IAbpDistributedLockHandle? distributedHandle = null;
        IDataPipelineExecutionFenceHandle? fenceHandle = null;
        var distributedLostToken = CancellationToken.None;
        var fenceLostToken = CancellationToken.None;
        try
        {
            // 首次读取只用于已收敛结论的无条件回放；所有会写终态的对账都必须
            // 在对应能力的 pin 生效后重新读取 execution 与目标资源，不能跨配置
            // 快照执行。ConfirmedApplied 仅需 Host Default，ConfirmedNotApplied
            // 还需与 AbpBackgroundJobs 同库。
            preparation = await PrepareResolutionAsync(
                executionId,
                resolution,
                topologyPhase,
                loadReceiptTarget: true,
                cancellationToken);
            if (preparation.ExistingResolution)
            {
                return preparation.Execution;
            }

            distributedHandle = await TryAcquireDistributedLockAsync(
                preparation.Execution.PipelineId,
                cancellationToken);
            if (distributedHandle == null)
            {
                RecordAdmission("busy");
                throw new BusinessException(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionBusy);
            }

            distributedLostToken = GetHandleLostToken(distributedHandle);
            using var admissionCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken,
                    distributedLostToken);
            try
            {
                var fenceResult = await _databaseFence.TryAcquireAsync(
                    preparation.Execution.PipelineId,
                    preparation.TargetResources,
                    capacitySlots: 0,
                    admissionCancellation.Token);
                fenceHandle = fenceResult.Handle;
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception fenceException)
            {
                RecordCoordinationEvent("database_fence_failure");
                _logger.LogError(
                    "ETL 对账 PostgreSQL session fence 获取失败 executionId={ExecutionId} exceptionType={ExceptionType}",
                    executionId,
                    fenceException.GetType().FullName ??
                    fenceException.GetType().Name);
                throw CoordinationUnavailable();
            }

            if (fenceHandle == null)
            {
                admissionCancellation.Token.ThrowIfCancellationRequested();
                RecordCoordinationEvent("database_fence_conflict");
                throw CoordinationUnavailable();
            }

            fenceLostToken = fenceHandle.HandleLostToken;

            using var ownershipCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(
                    distributedLostToken,
                    fenceLostToken);
            using var resolutionCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken,
                    ownershipCancellation.Token);
            await using var receiptReadFence =
                await AcquireReceiptAbsenceFenceForManualResolutionAsync(
                    preparation,
                    resolutionCancellation.Token);
            var receiptAbsenceVerified = receiptReadFence != null;
            var execution = await ResolveWithRetryAsync(
                executionId,
                resolution,
                input.ProcessedRows,
                verificationNote,
                resolverId,
                receiptAbsenceVerified,
                resolutionCancellation.Token,
                ownershipCancellation.Token);
            await UpdateProjectionNoThrowAsync(execution);
            RecordResult(execution.Status);
            return execution;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            (distributedLostToken.IsCancellationRequested ||
             fenceLostToken.IsCancellationRequested))
        {
            RecordCoordinationEvent("ownership_lost");
            throw CoordinationUnavailable();
        }
        finally
        {
            if (fenceHandle != null)
            {
                try
                {
                    await fenceHandle.DisposeAsync();
                }
                catch (Exception fenceReleaseException)
                {
                    RecordCoordinationEvent("database_fence_release_failure");
                    _logger.LogError(
                        "ETL 对账 PostgreSQL session fence 释放失败 executionId={ExecutionId} exceptionType={ExceptionType}",
                        executionId,
                        fenceReleaseException.GetType().FullName ??
                        fenceReleaseException.GetType().Name);
                }
            }

            if (distributedHandle != null)
            {
                try
                {
                    await distributedHandle.DisposeAsync();
                }
                catch (Exception lockReleaseException)
                {
                    RecordCoordinationEvent("lock_release_failure");
                    _logger.LogError(
                        "ETL 对账 Redis 执行锁释放失败 executionId={ExecutionId} exceptionType={ExceptionType}",
                        executionId,
                        lockReleaseException.GetType().FullName ??
                        lockReleaseException.GetType().Name);
                }
            }

            terminalStoreScope.Dispose();
        }
    }

    private async Task<ResolutionPreparation> PrepareResolutionAsync(
        Guid executionId,
        PipelineExecutionResolution resolution,
        PipelineTerminalStoreTopologyPhase? requiredTopologyPhase,
        bool loadReceiptTarget,
        CancellationToken cancellationToken)
    {
        if (requiredTopologyPhase.HasValue)
        {
            _terminalStoreTopologyGuard.EnsureSafeScope(
                requiredTopologyPhase.Value);
        }

        using var unitOfWork = _unitOfWorkManager.Begin(
            requiresNew: true,
            isTransactional: false);
        var execution = await _executionRepository.GetAsync(
            executionId,
            includeDetails: false,
            cancellationToken: cancellationToken);
        if (IsExistingResolution(execution, resolution))
        {
            await unitOfWork.CompleteAsync(cancellationToken);
            return new ResolutionPreparation(
                execution,
                Array.Empty<string>(),
                null,
                null,
                ExistingResolution: true);
        }

        if (execution.Status != ExecutionStatus.OutcomeUnknown)
        {
            throw ResolutionInvalid();
        }

        if (HasRunningRecoveryMarker(execution) &&
            !HasCompleteRunningRecoveryMarker(execution))
        {
            throw CoordinationUnavailable();
        }

        var targetResources = GetTargetResources(execution);
        if (targetResources.Count == 0)
        {
            RecordCoordinationEvent("database_fence_failure");
            throw CoordinationUnavailable();
        }

        DataSource? receiptTarget = null;
        EtlExecutionContext? receiptContext = null;
        if (execution.EvidenceProtocol == 1)
        {
            if (!TryCreateReceiptContext(execution, out receiptContext))
            {
                throw ReceiptInconclusive();
            }

            if (resolution == PipelineExecutionResolution.ConfirmedApplied)
            {
                RecordCoordinationEvent(
                    "target_receipt_manual_override_blocked");
                throw ReceiptVerificationRequired();
            }

            if (loadReceiptTarget)
            {
                var targetId = ReadGuidProperty(
                    execution,
                    TargetDataSourceIdPropertyName);
                if (!targetId.HasValue)
                {
                    throw ReceiptInconclusive();
                }

                receiptTarget = await _sourceRepository.FindAsync(
                    targetId.Value,
                    includeDetails: false,
                    cancellationToken: cancellationToken);
                // IsEnabled 只阻止新激活/执行；历史 receipt-v1 的 absent
                // break-glass 仍须可核验已禁用目标，并继续执行全部身份、
                // TLS、双 fence 与持锁回执缺失校验。
                if (receiptTarget == null ||
                    receiptTarget.Type !=
                    DataSourceType.RelationalDatabase)
                {
                    throw ReceiptInconclusive();
                }
            }
        }

        await unitOfWork.CompleteAsync(cancellationToken);
        return new ResolutionPreparation(
            execution,
            targetResources,
            receiptTarget,
            receiptContext,
            ExistingResolution: false);
    }

    private async Task<PipelineExecution> ResolveWithRetryAsync(
        Guid executionId,
        PipelineExecutionResolution resolution,
        long? processedRows,
        string verificationNote,
        Guid resolverId,
        bool receiptAbsenceVerified,
        CancellationToken cancellationToken,
        CancellationToken ownershipToken)
    {
        try
        {
            return await ResolveCoreAsync(
                executionId,
                resolution,
                processedRows,
                verificationNote,
                resolverId,
                receiptAbsenceVerified,
                cancellationToken);
        }
        catch (PipelineExecutionStateCommitUnknownException)
        {
            RecordCoordinationEvent("state_commit_outcome_unknown");
            try
            {
                ownershipToken.ThrowIfCancellationRequested();
                return await ResolveCoreAsync(
                    executionId,
                    resolution,
                    processedRows,
                    verificationNote,
                    resolverId,
                    receiptAbsenceVerified,
                    ownershipToken);
            }
            catch (PipelineExecutionStateCommitUnknownException)
            {
                _logger.LogCritical(
                    "ETL 对账终态连续两次无法确认 executionId={ExecutionId}；保持禁止重跑",
                    executionId);
                throw CoordinationUnavailable();
            }
        }
    }

    private async Task<PipelineExecution> ResolveCoreAsync(
        Guid executionId,
        PipelineExecutionResolution resolution,
        long? processedRows,
        string verificationNote,
        Guid resolverId,
        bool receiptAbsenceVerified,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(
            requiresNew: true,
            isTransactional: true);
        _terminalStoreTopologyGuard.EnsureSafeScope(
            GetResolutionTopologyPhase(resolution));

        var execution = await _executionRepository.GetAsync(
            executionId,
            includeDetails: false,
            cancellationToken: cancellationToken);
        if (IsExistingResolution(execution, resolution))
        {
            await CompleteUnitOfWorkAsync(
                unitOfWork,
                "resolution_idempotent");
            return execution;
        }

        if (execution.Status != ExecutionStatus.OutcomeUnknown)
        {
            throw ResolutionInvalid();
        }

        if (execution.EvidenceProtocol == 1 &&
            (resolution !=
                 PipelineExecutionResolution.ConfirmedNotApplied ||
             !receiptAbsenceVerified))
        {
            throw ReceiptVerificationRequired();
        }

        PipelineExecutionFailurePolicy.FailureDescriptor? failure = null;
        var resolvedAt = DateTime.UtcNow;
        switch (resolution)
        {
            case PipelineExecutionResolution.ConfirmedApplied:
                execution.ResolveAsSucceeded(resolvedAt, processedRows);
                break;
            case PipelineExecutionResolution.ConfirmedNotApplied:
                failure = _failurePolicy.RecordReconciledNotApplied(
                    execution,
                    resolvedAt);
                break;
            default:
                throw ResolutionInvalid();
        }

        execution.SetProperty(ResolutionPropertyName, resolution.ToString());
        execution.SetProperty(
            ResolvedAtPropertyName,
            resolvedAt.ToString("O"));
        execution.SetProperty(
            ResolvedByPropertyName,
            resolverId.ToString("N"));
        execution.SetProperty(
            VerificationNotePropertyName,
            verificationNote);
        if (execution.EvidenceProtocol == 1)
        {
            StoreReceiptManualNotAppliedAudit(execution, resolvedAt);
        }
        await _executionRepository.UpdateAsync(
            execution,
            autoSave: false,
            cancellationToken: cancellationToken);
        if (failure != null)
        {
            await _notificationScheduler.ScheduleAsync(
                execution.PipelineId,
                execution.Id,
                failure,
                outcomeUnknown: false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await CompleteUnitOfWorkAsync(unitOfWork, "resolution_terminal_state");
        return execution;
    }


    private static PipelineExecutionResolution RequireResolution(
        ResolvePipelineExecutionOutcomeInput input)
    {
        if (!input.Resolution.HasValue ||
            !Enum.IsDefined(input.Resolution.Value))
        {
            throw ResolutionInvalid();
        }

        return input.Resolution.Value;
    }

    private static bool IsExistingResolution(
        PipelineExecution execution,
        PipelineExecutionResolution resolution)
    {
        var expectedStatus = resolution switch
        {
            PipelineExecutionResolution.ConfirmedApplied =>
                ExecutionStatus.Succeeded,
            PipelineExecutionResolution.ConfirmedNotApplied =>
                ExecutionStatus.Failed,
            _ => (ExecutionStatus?)null,
        };
        return expectedStatus.HasValue &&
               execution.Status == expectedStatus.Value &&
               string.Equals(
                   ReadStringProperty(execution, ResolutionPropertyName),
                   resolution.ToString(),
                   StringComparison.Ordinal) &&
               (resolution !=
                    PipelineExecutionResolution.ConfirmedNotApplied ||
                HasCompleteReceiptManualNotAppliedAudit(execution));
    }

}
