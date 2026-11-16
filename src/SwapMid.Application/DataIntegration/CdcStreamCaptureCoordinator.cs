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

        CdcStreamDefinition stream;
        DataSource source;
        DataSource? targetSink = null;
        using (var readUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            stream = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
            if (requireActiveStatus && stream.Status != CdcStreamStatus.Active)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamNotActive);
            }

            source = await _sourceRepository.GetAsync(stream.SourceId, cancellationToken: cancellationToken);
            if (stream.TargetMode == CdcTargetMode.ExternalSink && stream.TargetSinkId.HasValue)
            {
                targetSink = await _sourceRepository.FindAsync(
                    stream.TargetSinkId.Value,
                    cancellationToken: cancellationToken);
            }

            await readUow.CompleteAsync(cancellationToken);
        }

        try
        {
            var batch = await _captureService.CaptureBatchAsync(source, stream, targetSink, cancellationToken);

            using (var writeUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                var latest = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
                latest.MarkCaptureSucceeded(batch.NewCheckpointLsn, batch.CapturedEventCount, batch.CapturedAtUtc);
                await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
                await writeUow.CompleteAsync(cancellationToken);
            }

            return new CdcCaptureResultDto
            {
                StreamId = id,
                PreviousCheckpointLsn = batch.PreviousCheckpointLsn,
                NewCheckpointLsn = batch.NewCheckpointLsn,
                CapturedEventCount = batch.CapturedEventCount,
                DeliveredCount = batch.DeliveredCount,
                CapturedAtUtc = batch.CapturedAtUtc,
                Message = batch.Message,
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
}
