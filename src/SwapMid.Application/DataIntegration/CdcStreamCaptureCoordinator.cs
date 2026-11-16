using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

/// <summary>
/// 协调 CDC 流 WAL 有界捕获、分布式互斥锁保护及状态/断点原子提交。
/// </summary>
public sealed class CdcStreamCaptureCoordinator : ITransientDependency
{
    public const string DistributedLockPrefix = "swapmid:data-integration:cdc:";

    private readonly ICdcStreamRepository _streamRepository;
    private readonly IRepository<DataSource, Guid> _sourceRepository;
    private readonly PgOutputCdcCaptureService _captureService;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly PipelineExecutionFailurePolicy _failurePolicy;
    private readonly ILogger<CdcStreamCaptureCoordinator> _logger;

    public CdcStreamCaptureCoordinator(
        ICdcStreamRepository streamRepository,
        IRepository<DataSource, Guid> sourceRepository,
        PgOutputCdcCaptureService captureService,
        IAbpDistributedLock distributedLock,
        IUnitOfWorkManager unitOfWorkManager,
        PipelineExecutionFailurePolicy failurePolicy,
        ILogger<CdcStreamCaptureCoordinator> logger)
    {
        _streamRepository = streamRepository;
        _sourceRepository = sourceRepository;
        _captureService = captureService;
        _distributedLock = distributedLock;
        _unitOfWorkManager = unitOfWorkManager;
        _failurePolicy = failurePolicy;
        _logger = logger;
    }

    public async Task<CdcCaptureResultDto> CaptureAsync(
        Guid id,
        bool requireActiveStatus,
        CancellationToken cancellationToken = default)
    {
        await using var handle = await _distributedLock.TryAcquireAsync(
            DistributedLockPrefix + id.ToString("N"),
            TimeSpan.Zero,
            cancellationToken);
        if (handle == null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamCaptureBusy);
        }

        var (stream, source, targetSink) = await LoadExecutionInputsAsync(id, requireActiveStatus, cancellationToken);

        try
        {
            PgOutputSnapshotBatchResult? snapshotResult = null;
            if (stream.InitialSnapshotEnabled && stream.SnapshotStatus == CdcSnapshotStatus.NotStarted)
            {
                try
                {
                    snapshotResult = await _captureService.SnapshotAsync(source, stream, targetSink, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    var failure = _failurePolicy.DescribeFailure(ex, id);
                    await PersistSnapshotFailureAsync(id, failure, cancellationToken);
                    throw;
                }

                stream = await PersistSnapshotSuccessAsync(id, snapshotResult, cancellationToken);
            }

            var batch = await _captureService.CaptureBatchAsync(source, stream, targetSink, cancellationToken);

            using (var writeUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                var latest = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
                latest.MarkCaptureSucceeded(batch.NewCheckpointLsn, batch.CapturedEventCount, batch.CapturedAtUtc);
                await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
                await writeUow.CompleteAsync(cancellationToken);
            }

            var totalEvents = (snapshotResult?.SnapshotRowCount ?? 0) + batch.CapturedEventCount;
            var totalDelivered = (snapshotResult?.DeliveredCount ?? 0) + batch.DeliveredCount;
            var message = snapshotResult != null
                ? $"{snapshotResult.Message} {batch.Message}"
                : batch.Message;

            return new CdcCaptureResultDto
            {
                StreamId = id,
                PreviousCheckpointLsn = batch.PreviousCheckpointLsn,
                NewCheckpointLsn = batch.NewCheckpointLsn,
                CapturedEventCount = totalEvents,
                DeliveredCount = totalDelivered,
                CapturedAtUtc = batch.CapturedAtUtc,
                Message = message,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = _failurePolicy.DescribeFailure(ex, id);
            using (var errUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                var latest = await _streamRepository.FindAsync(id, cancellationToken: cancellationToken);
                if (latest != null)
                {
                    latest.MarkCaptureFailed(failure.ErrorCode, failure.ExceptionType);
                    await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
                }

                await errUow.CompleteAsync(cancellationToken);
            }

            _logger.LogWarning(
                "CDC 流 {StreamId} 捕获失败：code={Code} correlationId={CorrelationId} exceptionType={Type}",
                id,
                failure.ErrorCode,
                failure.CorrelationId,
                failure.ExceptionType);
            throw;
        }
    }

    public async Task<CdcSnapshotResultDto> SnapshotAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var handle = await _distributedLock.TryAcquireAsync(
            DistributedLockPrefix + id.ToString("N"),
            TimeSpan.Zero,
            cancellationToken);
        if (handle == null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamCaptureBusy);
        }

        var (stream, source, targetSink) = await LoadExecutionInputsAsync(id, requireActiveStatus: false, cancellationToken);

        try
        {
            var snapshotResult = await _captureService.SnapshotAsync(source, stream, targetSink, cancellationToken);
            await PersistSnapshotSuccessAsync(id, snapshotResult, cancellationToken);
            return new CdcSnapshotResultDto
            {
                StreamId = id,
                ConsistentPointLsn = snapshotResult.ConsistentPointLsn,
                SnapshotRowCount = snapshotResult.SnapshotRowCount,
                DeliveredCount = snapshotResult.DeliveredCount,
                UsedExportedSnapshot = snapshotResult.UsedExportedSnapshot,
                CompletedAtUtc = snapshotResult.CompletedAtUtc,
                Message = snapshotResult.Message,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = _failurePolicy.DescribeFailure(ex, id);
            await PersistSnapshotFailureAsync(id, failure, cancellationToken);
            throw;
        }
    }

    private async Task<(CdcStreamDefinition Stream, DataSource Source, DataSource? TargetSink)> LoadExecutionInputsAsync(
        Guid id,
        bool requireActiveStatus,
        CancellationToken cancellationToken)
    {
        using var readUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var stream = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
        if (requireActiveStatus && stream.Status != CdcStreamStatus.Active)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamNotActive);
        }

        var source = await _sourceRepository.GetAsync(stream.SourceId, cancellationToken: cancellationToken);
        DataSource? targetSink = null;
        if (stream.TargetMode == CdcTargetMode.ExternalSink && stream.TargetSinkId.HasValue)
        {
            targetSink = await _sourceRepository.FindAsync(
                stream.TargetSinkId.Value,
                cancellationToken: cancellationToken);
        }

        await readUow.CompleteAsync(cancellationToken);
        return (stream, source, targetSink);
    }

    private async Task<CdcStreamDefinition> PersistSnapshotSuccessAsync(
        Guid id,
        PgOutputSnapshotBatchResult result,
        CancellationToken cancellationToken)
    {
        using var writeUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var latest = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
        latest.MarkSnapshotSucceeded(result.ConsistentPointLsn, result.SnapshotRowCount, result.CompletedAtUtc);
        await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
        await writeUow.CompleteAsync(cancellationToken);
        return latest;
    }

    private async Task PersistSnapshotFailureAsync(
        Guid id,
        PipelineExecutionFailurePolicy.FailureDescriptor failure,
        CancellationToken cancellationToken)
    {
        using var errUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var latest = await _streamRepository.FindAsync(id, cancellationToken: cancellationToken);
        if (latest != null)
        {
            latest.MarkSnapshotFailed(failure.ErrorCode, failure.ExceptionType);
            await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
        }

        await errUow.CompleteAsync(cancellationToken);
        _logger.LogWarning(
            "CDC 流 {StreamId} 基线快照失败：code={Code} correlationId={CorrelationId} exceptionType={Type}",
            id,
            failure.ErrorCode,
            failure.CorrelationId,
            failure.ExceptionType);
    }
}
