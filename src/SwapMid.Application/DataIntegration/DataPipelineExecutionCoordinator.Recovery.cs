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
    public const string RecoveryRequestIdPropertyName =
        "PipelineExecution.RecoveryRequestId";
    public const string RecoveredAtPropertyName =
        "PipelineExecution.RecoveredAt";
    public const string RecoveredByPropertyName =
        "PipelineExecution.RecoveredBy";
    public const string RecoveryExpectedConcurrencyStampPropertyName =
        "PipelineExecution.RecoveryExpectedConcurrencyStamp";
    public const string RecoveryDrainStartedAtPropertyName =
        "PipelineExecution.RecoveryDrainStartedAt";
    public const string RecoveryOldOwnerExitVerifiedAtPropertyName =
        "PipelineExecution.RecoveryOldOwnerExitVerifiedAt";
    public const string RecoveryTargetSessionExitVerifiedAtPropertyName =
        "PipelineExecution.RecoveryTargetSessionExitVerifiedAt";
    public const string RecoveryEvidenceReferencePropertyName =
        "PipelineExecution.RecoveryEvidenceReference";
    public const string RecoveryProtocolVersionPropertyName =
        "PipelineExecution.RecoveryProtocolVersion";

    private const string RecoveryProtocolVersion = "1";
    private static readonly TimeSpan MinimumRecoveryDrain =
        TimeSpan.FromMinutes(6);

    /// <summary>
    /// 在完整摘流、确认所有旧 owner 退出并由 DBA 确认目标活跃会话/事务消失后，
    /// 将进程崩溃遗留的 Running 受控收敛为 OutcomeUnknown。它只改变平台侧可人工
    /// 对账状态，绝不根据 TTL、锁消失或时间推断外部目标事务结果。
    /// </summary>
    public async Task<PipelineExecution> RecoverRunningAsync(
        Guid executionId,
        RecoverPipelineExecutionInput input,
        Guid recovererId,
        CancellationToken cancellationToken = default)
    {
        EnsureNoAmbientUnitOfWork();
        if (executionId == Guid.Empty ||
            input == null ||
            input.RecoveryRequestId == Guid.Empty)
        {
            throw RecoveryInvalid();
        }

        using var hostScope = _currentTenant.Change(null);
        var validationNow = DateTimeOffset.UtcNow;

        // 第一次只读允许已提交请求在终态拓扑暂不可用时幂等回放；任何新恢复都必须
        // 在连接 pin 生效后重新读取 pipeline 与持久 TargetResource。
        var preparation = await PrepareRecoveryAsync(
            executionId,
            input,
            recovererId,
            validationNow,
            cancellationToken);
        if (preparation.ExistingRecovery)
        {
            return preparation.Execution;
        }

        var recovery = preparation.Recovery;

        var terminalStorePin = await _terminalStoreTopologyGuard
            .CaptureSafeAsync(
                PipelineTerminalStoreTopologyPhase.RunningRecovery,
                cancellationToken);
        using var terminalStoreScope = _terminalStoreTopologyGuard
            .EnterSafeScope(
                terminalStorePin,
                PipelineTerminalStoreTopologyPhase.RunningRecovery);

        preparation = await PrepareRecoveryAsync(
            executionId,
            input,
            recovererId,
            validationNow,
            cancellationToken);
        if (preparation.ExistingRecovery)
        {
            return preparation.Execution;
        }
        recovery = preparation.Recovery;

        IAbpDistributedLockHandle? distributedHandle = null;
        IDataPipelineExecutionFenceHandle? fenceHandle = null;
        var distributedLostToken = CancellationToken.None;
        var fenceLostToken = CancellationToken.None;
        try
        {
            distributedHandle = await TryAcquireDistributedLockAsync(
                preparation.PipelineId,
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
                    preparation.PipelineId,
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
                    "ETL Running 恢复 PostgreSQL session fence 获取失败 executionId={ExecutionId} exceptionType={ExceptionType}",
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
            using var recoveryCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken,
                    ownershipCancellation.Token);

            var result = await RecoverWithRetryAsync(
                executionId,
                preparation.PipelineId,
                preparation.TargetResources,
                recovery,
                recoveryCancellation.Token,
                ownershipCancellation.Token);
            if (!result.ExistingRecovery)
            {
                await UpdateProjectionNoThrowAsync(result.Execution);
                RecordCoordinationEvent("running_recovery_succeeded");
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
                    RecordCoordinationEvent("database_fence_release_failure");
                    _logger.LogError(
                        "ETL Running 恢复 PostgreSQL session fence 释放失败 executionId={ExecutionId} exceptionType={ExceptionType}",
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
                        "ETL Running 恢复 Redis 执行锁释放失败 executionId={ExecutionId} exceptionType={ExceptionType}",
                        executionId,
                        lockReleaseException.GetType().FullName ??
                        lockReleaseException.GetType().Name);
                }
            }
        }
    }

    private async Task<RecoveryPreparation> PrepareRecoveryAsync(
        Guid executionId,
        RecoverPipelineExecutionInput input,
        Guid recovererId,
        DateTimeOffset validationNow,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(
            requiresNew: true,
            isTransactional: false);
        var execution = await _executionRepository.GetAsync(
            executionId,
            includeDetails: false,
            cancellationToken: cancellationToken);
        var hasRecoveryMarker = HasRunningRecoveryMarker(execution);
        if (hasRecoveryMarker &&
            !HasCompleteRunningRecoveryMarker(execution))
        {
            // 持久 marker 的完整性优先于本次 payload 校验；库损坏不能被畸形
            // 重试载荷掩盖成普通幂等冲突。
            throw CoordinationUnavailable();
        }

        var sameRequestId = HasSameRecoveryRequestId(
            execution,
            input.RecoveryRequestId);
        if (hasRecoveryMarker && !sameRequestId)
        {
            throw RecoveryConflict();
        }

        NormalizedRecovery recovery;
        try
        {
            recovery = NormalizeRecoveryInput(
                input,
                recovererId,
                execution.StartedAt,
                validationNow);
        }
        catch (BusinessException) when (sameRequestId)
        {
            // requestId 已占用时，畸形/缺失的重试载荷也属于幂等键冲突，不能
            // 退化为“修正字段后继续复用同一 ID”。NormalizeRecoveryInput 的
            // 唯一业务异常是 RecoveryInvalid；这里故意不绑定异常对象，维持 ETL
            // 失败边界“不得读取异常正文或把异常对象交给日志”的静态契约。
            throw RecoveryConflict();
        }

        if (IsExistingRecovery(execution, recovery))
        {
            await unitOfWork.CompleteAsync(cancellationToken);
            return new RecoveryPreparation(
                execution,
                execution.PipelineId,
                Array.Empty<string>(),
                recovery,
                ExistingRecovery: true);
        }

        // 恢复只依赖 execution 内的 PipelineId 与执行时目标快照：DataPipeline 是
        // soft-delete 聚合，遗留 Running 不能因当前管道已删除而永久失去处置路径；
        // 更不能回查当前 DataSource/TransformConfig 冒充历史事实。
        var targetResources = GetTargetResources(execution);
        if (targetResources.Count == 0)
        {
            RecordCoordinationEvent("database_fence_failure");
            throw CoordinationUnavailable();
        }

        await unitOfWork.CompleteAsync(cancellationToken);
        return new RecoveryPreparation(
            execution,
            execution.PipelineId,
            targetResources,
            recovery,
            ExistingRecovery: false);
    }

    private async Task<RecoveryResult> RecoverWithRetryAsync(
        Guid executionId,
        Guid pipelineId,
        IReadOnlyCollection<string> targetResources,
        NormalizedRecovery recovery,
        CancellationToken cancellationToken,
        CancellationToken ownershipToken)
    {
        async Task<RecoveryResult> CommitAsync(
            CancellationToken commitCancellationToken)
        {
            using var unitOfWork = _unitOfWorkManager.Begin(
                requiresNew: true,
                isTransactional: true);
            _terminalStoreTopologyGuard.EnsureSafeScope(
                PipelineTerminalStoreTopologyPhase.RunningRecovery);

            // 生产仓储只按 Id 发参数化 SELECT ... FOR UPDATE；请求 marker、状态和
            // ConcurrencyStamp 的分类必须全部发生在取得该行锁之后。
            var execution = await _executionRepository.GetForUpdateAsync(
                executionId,
                commitCancellationToken);
            if (IsExistingRecovery(execution, recovery))
            {
                await unitOfWork.CompleteAsync(commitCancellationToken);
                return new RecoveryResult(
                    execution,
                    ExistingRecovery: true);
            }

            if (HasRunningRecoveryMarker(execution))
            {
                if (!HasCompleteRunningRecoveryMarker(execution))
                {
                    throw CoordinationUnavailable();
                }

                throw RecoveryConflict();
            }

            if (execution.Status != ExecutionStatus.Running)
            {
                throw RecoveryInvalid();
            }

            if (!string.Equals(
                    execution.ConcurrencyStamp,
                    recovery.ExpectedConcurrencyStamp,
                    StringComparison.Ordinal))
            {
                throw RecoveryConflict();
            }

            if (execution.PipelineId != pipelineId ||
                GetTargetResources(execution).Count != targetResources.Count ||
                GetTargetResources(execution).Any(value =>
                    !targetResources.Contains(value)))
            {
                RecordCoordinationEvent("database_fence_failure");
                throw CoordinationUnavailable();
            }

            var recoveredAt = DateTimeOffset.UtcNow;
            var failure = _failurePolicy
                .RecordControlledRecoveryOutcomeUnknown(
                    execution,
                    recoveredAt.UtcDateTime);
            StoreRecoveryAudit(
                execution,
                recovery,
                recoveredAt);

            // 不手工改 ConcurrencyStamp；ABP SaveChanges 会在本次真实更新时自动轮换。
            await _executionRepository.UpdateAsync(
                execution,
                autoSave: false,
                commitCancellationToken);
            await _notificationScheduler.ScheduleAsync(
                execution.PipelineId,
                execution.Id,
                failure,
                outcomeUnknown: true);

            commitCancellationToken.ThrowIfCancellationRequested();
            await CompleteUnitOfWorkAsync(
                unitOfWork,
                "running_recovery_terminal_state");
            return new RecoveryResult(
                execution,
                ExistingRecovery: false);
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
                // ACK 丢失确认重试不再受 caller 断线影响，但 Redis/PG 任一执行权
                // 已失效时绝不能把它降格成一笔无锁的新恢复。
                ownershipToken.ThrowIfCancellationRequested();
                var replay = await CommitAsync(ownershipToken);
                // 请求在首次尝试前尚无 marker；持锁重读看到同一 marker，说明首次
                // 提交已生效。对调用方算一次成功，而通知不会再次插入。
                return replay with { ExistingRecovery = false };
            }
            catch (PipelineExecutionStateCommitUnknownException)
            {
                _logger.LogCritical(
                    "ETL Running 恢复连续两次无法确认 executionId={ExecutionId}；保持禁止重跑",
                    executionId);
                throw CoordinationUnavailable();
            }
        }
    }

    private static void StoreRecoveryAudit(
        PipelineExecution execution,
        NormalizedRecovery recovery,
        DateTimeOffset recoveredAt)
    {
        execution.SetProperty(
            RecoveryRequestIdPropertyName,
            recovery.RequestId.ToString("N"));
        execution.SetProperty(
            RecoveredAtPropertyName,
            recoveredAt.ToUniversalTime().ToString("O"));
        execution.SetProperty(
            RecoveredByPropertyName,
            recovery.RecovererId.ToString("N"));
        execution.SetProperty(
            RecoveryExpectedConcurrencyStampPropertyName,
            recovery.ExpectedConcurrencyStamp);
        execution.SetProperty(
            RecoveryDrainStartedAtPropertyName,
            recovery.DrainStartedAt.ToString("O"));
        execution.SetProperty(
            RecoveryOldOwnerExitVerifiedAtPropertyName,
            recovery.OldOwnerExitVerifiedAt.ToString("O"));
        execution.SetProperty(
            RecoveryTargetSessionExitVerifiedAtPropertyName,
            recovery.TargetSessionExitVerifiedAt.ToString("O"));
        execution.SetProperty(
            RecoveryEvidenceReferencePropertyName,
            recovery.EvidenceReference);
        execution.SetProperty(
            RecoveryProtocolVersionPropertyName,
            recovery.ProtocolVersion);
    }

}
