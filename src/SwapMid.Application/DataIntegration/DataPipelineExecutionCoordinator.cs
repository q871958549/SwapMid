using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;


/// <summary>
/// 数据管道唯一执行协调器：执行权覆盖 Running 声明、事务外 ETL 和终态提交。任何入口都必须
/// 经过此类，禁止直接调用 <see cref="IEtlPipelineRunner"/>。
/// </summary>
public sealed partial class DataPipelineExecutionCoordinator : ITransientDependency
{
    private const int MaxDurableStateScan = 1_024;

    public async Task<PipelineExecution> ExecuteAsync(
        Guid pipelineId,
        Guid requestedRunId,
        TriggerType triggerType,
        CancellationToken cancellationToken = default)
    {
        EnsureNoAmbientUnitOfWork();
        if (requestedRunId == Guid.Empty)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionInputInvalid);
        }

        // 数据集成实体与其 PostgreSQL fence 都属于 host；入口一次性切 host，
        // 避免 ConfirmedApplied 等不需要通知 pin 的路径按 caller tenant 取错 fence。
        using var hostScope = _currentTenant.Change(null);

        var executionId = requestedRunId;
        IAbpDistributedLockHandle? distributedHandle = null;
        IDataPipelineExecutionFenceHandle? fenceHandle = null;
        IDisposable? terminalStoreScope = null;
        var distributedLostToken = CancellationToken.None;
        var fenceLostToken = CancellationToken.None;
        try
        {
            distributedHandle = await TryAcquireDistributedLockAsync(
                pipelineId,
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
            var admissionToken = admissionCancellation.Token;

            var preparation = await PrepareAsync(
                pipelineId,
                executionId,
                admissionToken);
            if (preparation.ExistingExecution is
                { Status: not ExecutionStatus.Running } existing)
            {
                RecordAdmission("idempotent_replay");
                return existing;
            }

            try
            {
                var terminalStorePin = await _terminalStoreTopologyGuard
                    .CaptureSafeAsync(
                    PipelineTerminalStoreTopologyPhase.ExecutionAdmission,
                    admissionToken);
                terminalStoreScope = _terminalStoreTopologyGuard
                    .EnterSafeScope(
                        terminalStorePin,
                        PipelineTerminalStoreTopologyPhase.ExecutionAdmission);
            }
            catch (BusinessException)
            {
                RecordAdmission("coordination_failure");
                throw;
            }

            // 首次 Prepare 只用于让已终态 RunId 在队列拓扑异常时仍可回放；
            // 新执行必须在 pin 生效后重新读取，禁止把旧库快照带入新库的 fence/Claim。
            preparation = await PrepareAsync(
                pipelineId,
                executionId,
                admissionToken);
            if (preparation.ExistingExecution is
                { Status: not ExecutionStatus.Running } pinnedExisting)
            {
                RecordAdmission("idempotent_replay");
                return pinnedExisting;
            }

            DataPipelineExecutionFenceAcquireResult fenceResult;
            try
            {
                fenceResult = await _databaseFence.TryAcquireAsync(
                    pipelineId,
                    preparation.Identity!.TargetResources,
                    _options.MaxConcurrentExecutions,
                    admissionToken);
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
                    "ETL PostgreSQL session fence 获取失败 pipelineId={PipelineId} exceptionType={ExceptionType}",
                    pipelineId,
                    fenceException.GetType().FullName ??
                    fenceException.GetType().Name);
                throw CoordinationUnavailable();
            }

            fenceHandle = fenceResult.Handle;
            if (fenceHandle == null &&
                fenceResult.Rejection ==
                DataPipelineExecutionFenceRejection.CapacityExhausted)
            {
                RecordAdmission("capacity_exhausted");
                throw new BusinessException(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionCapacityExhausted);
            }

            if (fenceHandle == null)
            {
                admissionToken.ThrowIfCancellationRequested();
                RecordCoordinationEvent("database_fence_conflict");
                throw CoordinationUnavailable();
            }

            fenceLostToken = fenceHandle.HandleLostToken;

            using var executionCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken,
                    distributedLostToken,
                    fenceLostToken);
            var executionToken = executionCancellation.Token;

            Claim claim;
            try
            {
                claim = await ClaimAsync(
                    pipelineId,
                    executionId,
                    triggerType,
                    preparation.Identity!.TargetResources,
                    executionToken);
            }
            catch (PipelineExecutionStateCommitUnknownException)
            {
                RecordCoordinationEvent("state_commit_outcome_unknown");
                throw CoordinationUnavailable();
            }
            if (!claim.ShouldExecute)
            {
                await UpdateProjectionNoThrowAsync(claim.Execution);
                if (claim.OutcomeUnresolved)
                {
                    throw new BusinessException(
                        SwapMidDomainErrorCodes.DataIntegration
                            .PipelineExecutionOutcomeUnresolved);
                }

                return claim.Execution;
            }

            RecordAdmission("acquired");
            await UpdateProjectionNoThrowAsync(claim.Execution);

            EtlRunResult result;
            try
            {
                result = await _etlRunner.RunAsync(
                    claim.ExecutionContext!,
                    claim.Source,
                    claim.Target,
                    claim.TransformConfig,
                    executionToken);
            }
            catch (EtlTargetCommitOutcomeUnknownException targetCommitException)
            {
                RecordCoordinationEvent("target_commit_outcome_unknown");
                var failure = _failurePolicy.DescribeOutcomeUnknown(
                    targetCommitException,
                    executionId);
                return await FinalizeAsync(
                    executionId,
                    TerminalKind.OutcomeUnknown,
                    processedRows: null,
                    failure,
                    targetCommitException.ReceiptEvidence);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                await FinalizeAsync(
                    executionId,
                    TerminalKind.Cancelled,
                    processedRows: null,
                    failure: null);
                throw;
            }
            catch (Exception executionException) when (
                distributedLostToken.IsCancellationRequested ||
                fenceLostToken.IsCancellationRequested)
            {
                RecordCoordinationEvent("ownership_lost");
                var failure = _failurePolicy.DescribeCoordinationFailure(
                    executionException,
                    executionId);
                return await FinalizeAsync(
                    executionId,
                    TerminalKind.CoordinationFailed,
                    processedRows: null,
                    failure);
            }
            catch (Exception executionException)
            {
                var failure = _failurePolicy.DescribeFailure(
                    executionException,
                    executionId);
                return await FinalizeAsync(
                    executionId,
                    TerminalKind.Failed,
                    processedRows: null,
                    failure);
            }

            // Runner 只有在目标 COMMIT 已确认成功后才返回。成功终态提交必须位于 runner 的
            // catch 边界之外：即使平台状态提交暂不可确认，也绝不能再落入普通失败分支，
            // 把已经写入目标库的任务记成 Failed 后放行重跑。
            return await FinalizeAsync(
                executionId,
                TerminalKind.Succeeded,
                result.Rows,
                failure: null,
                result.ReceiptEvidence);
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
                        "ETL PostgreSQL session fence 释放失败 pipelineId={PipelineId} exceptionType={ExceptionType}",
                        pipelineId,
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
                        "ETL Redis 执行锁释放失败 pipelineId={PipelineId} exceptionType={ExceptionType}",
                        pipelineId,
                        lockReleaseException.GetType().FullName ??
                        lockReleaseException.GetType().Name);
                }
            }

            terminalStoreScope?.Dispose();
        }
    }

    private async Task<Claim> ClaimAsync(
        Guid pipelineId,
        Guid executionId,
        TriggerType triggerType,
        System.Collections.Generic.IReadOnlyCollection<string>
            lockedTargetResources,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(
            requiresNew: true,
            isTransactional: true);
        _terminalStoreTopologyGuard.EnsureSafeScope(
            PipelineTerminalStoreTopologyPhase.Claim);
        var existingById = await _executionRepository.FindAsync(
            executionId,
            includeDetails: false,
            cancellationToken: cancellationToken);
        if (existingById != null && existingById.PipelineId != pipelineId)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionInputInvalid);
        }

        if (existingById is { Status: not ExecutionStatus.Running })
        {
            await unitOfWork.CompleteAsync(cancellationToken);
            return Claim.Replay(existingById);
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
        var executionIdentity = BuildExecutionIdentity(
            pipeline,
            source,
            target);
        if (lockedTargetResources.Count !=
                executionIdentity.TargetResources.Count ||
            executionIdentity.TargetResources.Any(value =>
                !lockedTargetResources.Contains(value)))
        {
            throw CoordinationUnavailable();
        }

        // durable slot 的作用域是 pipeline + 物理目标，而不只是 pipeline。否则 A 管道
        // 对同一目标留下 OutcomeUnknown 后，B 管道仍可覆盖对账证据。
        var durableStates = await _executionRepository.GetDurableStatesAsync(
            MaxDurableStateScan + 1,
            cancellationToken);
        if (durableStates.Count > MaxDurableStateScan)
        {
            RecordCoordinationEvent("durable_state_scan_limit");
            _logger.LogCritical(
                "ETL durable state 扫描超过安全上限 countAtLeast={CountAtLeast} limit={Limit}",
                durableStates.Count,
                MaxDurableStateScan);
            throw CoordinationUnavailable();
        }

        var unresolved = durableStates
            .Where(item => item.Status == ExecutionStatus.OutcomeUnknown)
            .ToList();
        if (unresolved.Any(item =>
                HasMissingTargetResource(item) ||
                IsSamePipelineOrTarget(
                    item,
                    pipelineId,
                    lockedTargetResources)))
        {
            RecordCoordinationEvent("target_quarantine_blocked");
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionOutcomeUnresolved);
        }

        var running = durableStates
            .Where(item => item.Status == ExecutionStatus.Running)
            .ToList();
        // 旧协议 Running 没有目标摘要，无法证明它与当前目标无关；跨协议首发必须先 drain。
        // 这里选择 fail closed，避免用“查不到”误当“没有冲突”。
        if (running.Any(item =>
                item.PipelineId != pipelineId &&
                HasMissingTargetResource(item)))
        {
            RecordCoordinationEvent("legacy_durable_state_blocked");
            throw CoordinationUnavailable();
        }

        var durableRunning = running
            .Where(item => IsSamePipelineOrTarget(
                item,
                pipelineId,
                lockedTargetResources))
            .OrderBy(item => item.StartedAt)
            .ToList();
        if (durableRunning.Count > 0)
        {
            // 平台 Redis/PG fence 消失并不能证明已发往外部目标库的 SQL/COMMIT 已静默。
            // 因此绝不自动把 Running 改成 OutcomeUnknown；否则人工确认 NotApplied 后可能
            // 遭遇旧 owner 的晚到 COMMIT。保持 durable Running，fail closed 等原 owner
            // 明确终结；进程崩溃遗留项只能按运维 runbook drain 后恢复。
            RecordCoordinationEvent("running_durable_state_blocked");
            await unitOfWork.CompleteAsync(cancellationToken);
            var requestedExecution = durableRunning
                .SingleOrDefault(item => item.Id == executionId);
            if (requestedExecution != null &&
                durableRunning.Count == 1)
            {
                return Claim.Replay(requestedExecution);
            }

            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionTargetBusy);
        }

        if (existingById != null)
        {
            // 同 RunId 在本事务第一次读取后可能被原 owner 结算成终态，从而不再出现在
            // durableStates 的单查询快照中。只要 ID 已存在就绝不能再 Insert；保守回放本次
            // 跟踪到的 Running，调用方随后按同 RunId GET 会读到已提交终态。
            await unitOfWork.CompleteAsync(cancellationToken);
            return Claim.Replay(existingById);
        }

        var execution = new PipelineExecution(
            executionId,
            pipeline.Id,
            pipeline.Name,
            triggerType,
            executionIdentity.LegacyIdentity.Resource,
            executionIdentity.Activation?.VerifiedTargetResourceHash,
            executionIdentity.Activation == null ? (short)0 : (short)1,
            executionIdentity.Activation?.ResourceInstanceId,
            executionIdentity.ExecutionContractHash,
            executionIdentity.Activation?.TargetCoordinationIdentityHash);
        execution.SetProperty(
            TargetResourcePropertyName,
            executionIdentity.LegacyIdentity.Resource);
        StoreExecutionSnapshot(
            execution,
            source,
            target,
            executionIdentity.LegacyIdentity,
            executionIdentity.CanonicalTargetTable,
            executionIdentity.Activation != null);
        await _executionRepository.InsertAsync(
            execution,
            autoSave: false,
            cancellationToken);
        // 与 Running 声明同一短事务推进 pipeline 并发戳：配置更新/删除若与准入竞态，
        // 必须由 EF 乐观并发使其中一方失败，不能删掉刚声明的 durable slot 后仍启动 runner。
        pipeline.RecordExecution(execution.StartedAt, ExecutionStatus.Running);
        await _pipelineRepository.UpdateAsync(
            pipeline,
            autoSave: false,
            cancellationToken);
        await CompleteUnitOfWorkAsync(unitOfWork, "claim_running");
        return Claim.Start(
            execution,
            source,
            target,
            pipeline.TransformConfig,
            executionIdentity.CreateContext(executionId, pipeline.Id));
    }

}
