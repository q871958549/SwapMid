using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

/// <summary>
/// 管道激活边界：短平台读取 →（receipt-v1）UoW 外目标探针 → 短平台提交。
/// 外部数据库调用期间绝不持有平台事务。
/// </summary>
public sealed class DataPipelineActivationCoordinator : ITransientDependency
{
    private readonly IDataPipelineRepository _pipelineRepository;
    private readonly IRepository<DataSource, Guid> _sourceRepository;
    private readonly IEtlTargetReceiptStore _receiptStore;
    private readonly IPipelineTerminalStoreTopologyGuard
        _terminalStoreTopologyGuard;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataPipelineScheduleSynchronizer _scheduleSynchronizer;

    public DataPipelineActivationCoordinator(
        IDataPipelineRepository pipelineRepository,
        IRepository<DataSource, Guid> sourceRepository,
        IEtlTargetReceiptStore receiptStore,
        IPipelineTerminalStoreTopologyGuard terminalStoreTopologyGuard,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant,
        IDataPipelineScheduleSynchronizer? scheduleSynchronizer = null)
    {
        _pipelineRepository = pipelineRepository;
        _sourceRepository = sourceRepository;
        _receiptStore = receiptStore;
        _terminalStoreTopologyGuard = terminalStoreTopologyGuard;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
        _scheduleSynchronizer = scheduleSynchronizer ??
            NullDataPipelineScheduleSynchronizer.Instance;
    }

    internal async Task ActivateAsync(
        Guid pipelineId,
        CancellationToken cancellationToken)
    {
        if (_unitOfWorkManager.Current != null)
        {
            throw new InvalidOperationException(
                "ETL activation must manage its own short unit-of-work boundaries.");
        }

        using var hostScope = _currentTenant.Change(null);
        var terminalStorePin = await _terminalStoreTopologyGuard
            .CaptureSafeAsync(
                PipelineTerminalStoreTopologyPhase
                    .TargetReceiptActivation,
                cancellationToken);
        using var terminalStoreScope = _terminalStoreTopologyGuard
            .EnterSafeScope(
                terminalStorePin,
                PipelineTerminalStoreTopologyPhase
                    .TargetReceiptActivation);
        var preparation = await PrepareAsync(pipelineId, cancellationToken);
        EtlTargetReceiptActivation? receiptActivation = null;
        string? executionContractHash = null;
        if (preparation.Transform.ReconciliationStrategy ==
            EtlReconciliationStrategy.TargetReceiptV1)
        {
            try
            {
                receiptActivation = await _receiptStore.ProbeActivationAsync(
                    preparation.Target,
                    preparation.Transform,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (BusinessException exception) when (
                exception.Code ==
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionInputInvalid)
            {
                throw ReceiptInvalid();
            }
            catch (Exception)
            {
                throw new BusinessException(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineTargetReceiptUnavailable);
            }

            if (!string.Equals(
                    receiptActivation.Provider,
                    preparation.LegacyIdentity.Provider,
                    StringComparison.Ordinal))
            {
                throw ReceiptInvalid();
            }

            executionContractHash = EtlReceiptHashes.ComputeExecutionContract(
                preparation.Pipeline.Id,
                preparation.Source.Id,
                preparation.Source.ConcurrencyStamp,
                preparation.Target.Id,
                preparation.Target.ConcurrencyStamp,
                receiptActivation.Provider,
                receiptActivation.ResourceInstanceId,
                receiptActivation.TargetCoordinationIdentityHash,
                preparation.LegacyIdentity.Resource,
                receiptActivation.VerifiedTargetResourceHash,
                receiptActivation.CanonicalTargetTable,
                preparation.Transform.Mode,
                preparation.LegacyIdentity.SourceQuerySha256,
                preparation.Transform.KeyColumns,
                preparation.Transform.WatermarkColumn,
                preparation.Transform.ColumnMappings);
        }

        using var unitOfWork = _unitOfWorkManager.Begin(
            requiresNew: true,
            isTransactional: true);
        _terminalStoreTopologyGuard.EnsureSafeScope(
            PipelineTerminalStoreTopologyPhase.TargetReceiptActivation);
        DataPipelineActivationLock activationLock;
        try
        {
            activationLock = await _pipelineRepository
                .GetActivationForUpdateAsync(
                    pipelineId,
                    cancellationToken);
        }
        catch (EntityNotFoundException)
        {
            // 准备阶段之后发生的软删除/改绑只暴露稳定业务错误，不能把内部实体 ID
            // 或底层查询细节带到 API 错误正文。
            throw ReceiptInvalid();
        }

        var pipeline = activationLock.Pipeline;
        var source = activationLock.Source;
        var target = activationLock.Target;
        if (target == null ||
            pipeline.SourceId != preparation.Pipeline.SourceId ||
            pipeline.TargetId != preparation.Pipeline.TargetId ||
            !string.Equals(
                pipeline.ConcurrencyStamp,
                preparation.Pipeline.ConcurrencyStamp,
                StringComparison.Ordinal) ||
            !string.Equals(
                source.ConcurrencyStamp,
                preparation.Source.ConcurrencyStamp,
                StringComparison.Ordinal) ||
            !string.Equals(
                target.ConcurrencyStamp,
                preparation.Target.ConcurrencyStamp,
                StringComparison.Ordinal) ||
            !string.Equals(
                pipeline.TransformConfig,
                preparation.Pipeline.TransformConfig,
                StringComparison.Ordinal))
        {
            throw ReceiptInvalid();
        }

        DataPipelineReceiptActivation.Clear(pipeline);
        if (receiptActivation != null)
        {
            DataPipelineReceiptActivation.Store(
                pipeline,
                receiptActivation,
                executionContractHash!);
        }

        pipeline.Activate();
        await _pipelineRepository.UpdateAsync(
            pipeline,
            autoSave: false,
            cancellationToken: cancellationToken);
        await unitOfWork.CompleteAsync(cancellationToken);

        await _scheduleSynchronizer.SyncAsync(
            pipeline.Id,
            pipeline.Status,
            pipeline.ScheduleEnabled,
            pipeline.ScheduleCron,
            cancellationToken);
    }

    private async Task<Preparation> PrepareAsync(
        Guid pipelineId,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(
            requiresNew: true,
            isTransactional: false);
        _terminalStoreTopologyGuard.EnsureSafeScope(
            PipelineTerminalStoreTopologyPhase.TargetReceiptActivation);
        var pipeline = await _pipelineRepository.GetAsync(
            pipelineId,
            includeDetails: false,
            cancellationToken: cancellationToken);
        if (!pipeline.TargetId.HasValue)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineTargetNotConfigured);
        }

        if (pipeline.ScheduleEnabled)
        {
            DataPipelineCronValidator.ValidateAndNormalize(
                pipeline.ScheduleEnabled,
                pipeline.ScheduleCron);
        }

        var source = await _sourceRepository.GetAsync(
            pipeline.SourceId,
            includeDetails: false,
            cancellationToken: cancellationToken);
        var target = await _sourceRepository.GetAsync(
            pipeline.TargetId.Value,
            includeDetails: false,
            cancellationToken: cancellationToken);
        if (!source.IsEnabled || !target.IsEnabled ||
            source.Type != DataSourceType.RelationalDatabase ||
            target.Type != DataSourceType.RelationalDatabase)
        {
            throw ReceiptInvalid();
        }

        EtlTransformConfig transform;
        DataPipelineExecutionCoordinator.TargetIdentity legacyIdentity;
        try
        {
            transform = EtlTransformConfig.Parse(pipeline.TransformConfig);
            var sourceProbe = RelationalDataSourceConnection.Parse(source.Config);
            EtlSourceQueryGuard.Validate(
                transform.SourceQuery,
                sourceProbe.NormalizedProvider);
            legacyIdentity =
                DataPipelineExecutionCoordinator.ComputeTargetIdentity(
                    target.Config,
                    pipeline.TransformConfig);
        }
        catch (BusinessException)
        {
            throw ReceiptInvalid();
        }
        catch (JsonException)
        {
            throw ReceiptInvalid();
        }
        catch (InvalidOperationException)
        {
            throw ReceiptInvalid();
        }

        await unitOfWork.CompleteAsync(cancellationToken);
        return new Preparation(
            pipeline,
            source,
            target,
            transform,
            legacyIdentity);
    }

    private static BusinessException ReceiptInvalid() =>
        new(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineTargetReceiptInvalid);

    private sealed record Preparation(
        DataPipeline Pipeline,
        DataSource Source,
        DataSource Target,
        EtlTransformConfig Transform,
        DataPipelineExecutionCoordinator.TargetIdentity LegacyIdentity);
}
