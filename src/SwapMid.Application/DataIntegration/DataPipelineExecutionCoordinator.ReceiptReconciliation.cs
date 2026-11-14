using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

public sealed partial class DataPipelineExecutionCoordinator
{
    public const string TargetReceiptVerifiedResolution =
        "TargetReceiptVerifiedV1";
    public const string ReceiptVerificationSourcePropertyName =
        "PipelineExecution.ReceiptVerificationSource";
    public const string ReceiptVerifiedAtPropertyName =
        "PipelineExecution.ReceiptVerifiedAt";

    private const string ReceiptVerificationSource = "target-receipt-v1";
    private const string ReceiptVerificationNote = "target-receipt-v1";

    /// <summary>
    /// 只读核验目标库同事务回执。只有完整、逐字段匹配的 receipt-v1 才允许
    /// OutcomeUnknown → Succeeded；缺失、不匹配或不可达绝不推导为未应用。
    /// </summary>
    public async Task<PipelineExecution> VerifyTargetReceiptAsync(
        Guid executionId,
        Guid verifierId,
        CancellationToken cancellationToken = default)
    {
        EnsureNoAmbientUnitOfWork();
        if (executionId == Guid.Empty || verifierId == Guid.Empty)
        {
            throw ReceiptInconclusive();
        }

        using var hostScope = _currentTenant.Change(null);
        var terminalStorePin = await _terminalStoreTopologyGuard
            .CaptureSafeAsync(
                PipelineTerminalStoreTopologyPhase
                    .TargetReceiptVerification,
                cancellationToken);
        using var terminalStoreScope = _terminalStoreTopologyGuard
            .EnterSafeScope(
                terminalStorePin,
                PipelineTerminalStoreTopologyPhase
                    .TargetReceiptVerification);
        var preparation = await PrepareReceiptVerificationAsync(
            executionId,
            cancellationToken);
        if (preparation.ExistingVerification)
        {
            return preparation.Execution;
        }

        IAbpDistributedLockHandle? distributedHandle = null;
        IDataPipelineExecutionFenceHandle? fenceHandle = null;
        var distributedLostToken = CancellationToken.None;
        var fenceLostToken = CancellationToken.None;
        try
        {
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
                    "ETL 目标回执核验 PostgreSQL session fence 获取失败 executionId={ExecutionId} exceptionType={ExceptionType}",
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
            using var verificationCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken,
                    ownershipCancellation.Token);

            EtlTargetReceiptEvidence? receipt;
            try
            {
                receipt = await _targetReceiptStore.FindAsync(
                    preparation.Target,
                    preparation.Context,
                    verificationCancellation.Token);
            }
            catch (OperationCanceledException) when (
                verificationCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (BusinessException)
            {
                RecordCoordinationEvent("target_receipt_mismatch");
                throw ReceiptInconclusive();
            }
            catch (Exception targetException)
            {
                RecordCoordinationEvent("target_receipt_unavailable");
                _logger.LogWarning(
                    "ETL 目标回执暂不可核验 executionId={ExecutionId} exceptionType={ExceptionType}",
                    executionId,
                    targetException.GetType().FullName ??
                    targetException.GetType().Name);
                throw ReceiptUnavailable();
            }

            if (receipt == null)
            {
                RecordCoordinationEvent("target_receipt_absent");
                throw ReceiptInconclusive();
            }

            if (!ReceiptMatchesExecution(
                    preparation.Execution,
                    preparation.Context,
                    receipt))
            {
                RecordCoordinationEvent("target_receipt_mismatch");
                _logger.LogCritical(
                    "ETL 目标回执与平台执行证据不匹配 executionId={ExecutionId}",
                    executionId);
                throw ReceiptInconclusive();
            }

            var result = await ResolveReceiptWithRetryAsync(
                preparation,
                receipt,
                verifierId,
                verificationCancellation.Token,
                ownershipCancellation.Token);
            if (!result.ExistingVerification)
            {
                await UpdateProjectionNoThrowAsync(result.Execution);
                RecordCoordinationEvent("target_receipt_verified");
                RecordResult(result.Execution.Status);
            }

            return result.Execution;
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
                    RecordCoordinationEvent(
                        "database_fence_release_failure");
                    _logger.LogError(
                        "ETL 目标回执核验 fence 释放失败 executionId={ExecutionId} exceptionType={ExceptionType}",
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
                        "ETL 目标回执核验 Redis 锁释放失败 executionId={ExecutionId} exceptionType={ExceptionType}",
                        executionId,
                        lockReleaseException.GetType().FullName ??
                        lockReleaseException.GetType().Name);
                }
            }
        }
    }

    private async Task<ReceiptVerificationPreparation>
        PrepareReceiptVerificationAsync(
            Guid executionId,
            CancellationToken cancellationToken)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(
            requiresNew: true,
            isTransactional: false);
        _terminalStoreTopologyGuard.EnsureSafeScope(
            PipelineTerminalStoreTopologyPhase.TargetReceiptVerification);
        var execution = await _executionRepository.GetAsync(
            executionId,
            includeDetails: false,
            cancellationToken: cancellationToken);
        if (HasAnyReceiptVerificationMarker(execution))
        {
            if (!IsCompleteReceiptVerification(execution))
            {
                throw CoordinationUnavailable();
            }

            await unitOfWork.CompleteAsync(cancellationToken);
            return ReceiptVerificationPreparation.Replay(execution);
        }

        if (execution.Status != ExecutionStatus.OutcomeUnknown ||
            !TryCreateReceiptContext(execution, out var context))
        {
            throw ReceiptInconclusive();
        }

        var targetId = ReadGuidProperty(
            execution,
            TargetDataSourceIdPropertyName);
        if (!targetId.HasValue)
        {
            throw ReceiptInconclusive();
        }

        var target = await _sourceRepository.FindAsync(
            targetId.Value,
            includeDetails: false,
            cancellationToken: cancellationToken);
        // IsEnabled 只控制新激活/执行；禁用目标不能把已经进入隔离态的
        // receipt-v1 OutcomeUnknown 永久困住。历史核验仍须保留目标存在、
        // 类型、持久身份、TLS、双 fence 与逐字段回执校验。
        if (target == null ||
            target.Type != DataSourceType.RelationalDatabase)
        {
            throw ReceiptInconclusive();
        }

        var targetResources = GetTargetResources(execution);
        if (targetResources.Count != 2)
        {
            throw ReceiptInconclusive();
        }

        await unitOfWork.CompleteAsync(cancellationToken);
        return new ReceiptVerificationPreparation(
            execution,
            target,
            context,
            targetResources,
            execution.ConcurrencyStamp,
            ExistingVerification: false);
    }

    private async Task<ReceiptVerificationResult>
        ResolveReceiptWithRetryAsync(
            ReceiptVerificationPreparation preparation,
            EtlTargetReceiptEvidence receipt,
            Guid verifierId,
            CancellationToken cancellationToken,
            CancellationToken ownershipToken)
    {
        async Task<ReceiptVerificationResult> CommitAsync(
            CancellationToken commitCancellationToken)
        {
            using var unitOfWork = _unitOfWorkManager.Begin(
                requiresNew: true,
                isTransactional: true);
            _terminalStoreTopologyGuard.EnsureSafeScope(
                PipelineTerminalStoreTopologyPhase
                    .TargetReceiptVerification);
            var execution = await _executionRepository.GetForUpdateAsync(
                preparation.Execution.Id,
                commitCancellationToken);
            if (HasAnyReceiptVerificationMarker(execution))
            {
                if (!IsCompleteReceiptVerification(execution))
                {
                    throw CoordinationUnavailable();
                }

                await CompleteUnitOfWorkAsync(
                    unitOfWork,
                    "target_receipt_idempotent");
                return new ReceiptVerificationResult(
                    execution,
                    ExistingVerification: true);
            }

            if (execution.Status != ExecutionStatus.OutcomeUnknown ||
                !string.Equals(
                    execution.ConcurrencyStamp,
                    preparation.ExpectedConcurrencyStamp,
                    StringComparison.Ordinal) ||
                !TryCreateReceiptContext(execution, out var currentContext) ||
                currentContext != preparation.Context ||
                !ReceiptMatchesExecution(
                    execution,
                    currentContext,
                    receipt))
            {
                throw ReceiptInconclusive();
            }

            StoreReceiptEvidence(execution, receipt);
            var verifiedAt = DateTimeOffset.UtcNow;
            execution.ResolveAsSucceeded(
                verifiedAt.UtcDateTime,
                receipt.ProcessedRows);
            execution.SetProperty(
                ResolutionPropertyName,
                TargetReceiptVerifiedResolution);
            execution.SetProperty(
                ResolvedAtPropertyName,
                verifiedAt.ToString("O"));
            execution.SetProperty(
                ResolvedByPropertyName,
                verifierId.ToString("N"));
            execution.SetProperty(
                VerificationNotePropertyName,
                ReceiptVerificationNote);
            execution.SetProperty(
                ReceiptVerificationSourcePropertyName,
                ReceiptVerificationSource);
            execution.SetProperty(
                ReceiptVerifiedAtPropertyName,
                verifiedAt.ToString("O"));
            await _executionRepository.UpdateAsync(
                execution,
                autoSave: false,
                commitCancellationToken);
            commitCancellationToken.ThrowIfCancellationRequested();
            await CompleteUnitOfWorkAsync(
                unitOfWork,
                "target_receipt_terminal_state");
            return new ReceiptVerificationResult(
                execution,
                ExistingVerification: false);
        }

        try
        {
            return await CommitAsync(cancellationToken);
        }
        catch (PipelineExecutionStateCommitUnknownException)
        {
            RecordCoordinationEvent("state_commit_outcome_unknown");
            try
            {
                ownershipToken.ThrowIfCancellationRequested();
                var replay = await CommitAsync(ownershipToken);
                return replay with { ExistingVerification = false };
            }
            catch (PipelineExecutionStateCommitUnknownException)
            {
                _logger.LogCritical(
                    "ETL 目标回执终态连续两次无法确认 executionId={ExecutionId}；保持禁止重跑",
                    preparation.Execution.Id);
                throw CoordinationUnavailable();
            }
        }
    }

    private sealed record ReceiptVerificationPreparation(
        PipelineExecution Execution,
        DataSource Target,
        EtlExecutionContext Context,
        IReadOnlyCollection<string> TargetResources,
        string ExpectedConcurrencyStamp,
        bool ExistingVerification)
    {
        internal static ReceiptVerificationPreparation Replay(
            PipelineExecution execution) =>
            new(
                execution,
                null!,
                null!,
                Array.Empty<string>(),
                execution.ConcurrencyStamp,
                ExistingVerification: true);
    }

    private sealed record ReceiptVerificationResult(
        PipelineExecution Execution,
        bool ExistingVerification);
}
