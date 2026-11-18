using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SwapMid.Monitoring;
using SwapMid.Notification;
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
    private readonly SqlServerCdcCaptureService? _sqlServerCaptureService;
    private readonly MySqlBinlogCdcCaptureService? _mySqlCaptureService;
    private readonly CdcObjectStorageCompactionService? _lakehouseCompactor;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly PipelineExecutionFailurePolicy _failurePolicy;
    private readonly ILogger<CdcStreamCaptureCoordinator> _logger;
    private readonly ICdcScheduleSynchronizer _scheduleSynchronizer;
    private readonly INotificationService? _notificationService;

    public CdcStreamCaptureCoordinator(
        ICdcStreamRepository streamRepository,
        IRepository<DataSource, Guid> sourceRepository,
        PgOutputCdcCaptureService captureService,
        IAbpDistributedLock distributedLock,
        IUnitOfWorkManager unitOfWorkManager,
        PipelineExecutionFailurePolicy failurePolicy,
        ILogger<CdcStreamCaptureCoordinator> logger,
        ICdcScheduleSynchronizer? scheduleSynchronizer = null,
        INotificationService? notificationService = null,
        SqlServerCdcCaptureService? sqlServerCaptureService = null,
        CdcObjectStorageCompactionService? lakehouseCompactor = null,
        MySqlBinlogCdcCaptureService? mySqlCaptureService = null)
    {
        _streamRepository = streamRepository;
        _sourceRepository = sourceRepository;
        _captureService = captureService;
        _sqlServerCaptureService = sqlServerCaptureService;
        _mySqlCaptureService = mySqlCaptureService;
        _lakehouseCompactor = lakehouseCompactor;
        _distributedLock = distributedLock;
        _unitOfWorkManager = unitOfWorkManager;
        _failurePolicy = failurePolicy;
        _logger = logger;
        _scheduleSynchronizer = scheduleSynchronizer ?? NullCdcScheduleSynchronizer.Instance;
        _notificationService = notificationService;
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
                var preSnapSchema = await CdcStreamMappingHelpers.EvaluateSchemaForStreamAsync(
                    _captureService,
                    _sqlServerCaptureService,
                    _mySqlCaptureService,
                    source,
                    stream,
                    cancellationToken);
                stream = await HandleSchemaEvaluationAsync(stream, preSnapSchema, cancellationToken);

                try
                {
                    snapshotResult = await DispatchSnapshotAsync(source, stream, targetSink, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    var failure = _failurePolicy.DescribeFailure(ex, id);
                    await PersistSnapshotFailureAsync(id, failure, cancellationToken);
                    throw;
                }

                stream = await PersistSnapshotSuccessAsync(id, snapshotResult, cancellationToken);
            }

            var batch = await DispatchCaptureAsync(source, stream, targetSink, cancellationToken);

            using (var writeUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                var latest = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
                latest.MarkCaptureSucceeded(batch.NewCheckpointLsn, batch.CapturedEventCount, batch.CapturedAtUtc);
                await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
                await writeUow.CompleteAsync(cancellationToken);
            }

            if (targetSink != null && _lakehouseCompactor != null)
            {
                await _lakehouseCompactor.TryAutoCompactAsync(stream, targetSink, cancellationToken);
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
        catch (BusinessException ex) when (ex.Code == SwapMidDomainErrorCodes.DataIntegration.CdcStreamSchemaDriftDetected)
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
            var snapshotResult = await DispatchSnapshotAsync(source, stream, targetSink, cancellationToken);
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
        if (stream.TargetMode is (CdcTargetMode.ExternalSink or CdcTargetMode.RelationalTable) &&
            stream.TargetSinkId.HasValue)
        {
            targetSink = await _sourceRepository.FindAsync(
                stream.TargetSinkId.Value,
                cancellationToken: cancellationToken);
        }

        await readUow.CompleteAsync(cancellationToken);
        return (stream, source, targetSink);
    }

    private async Task<CdcStreamDefinition> HandleSchemaEvaluationAsync(
        CdcStreamDefinition stream,
        CdcSchemaDriftEvaluation eval,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stream.SchemaBaselineJson))
        {
            if (eval.CurrentJson != "[]")
            {
                using var initUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                var latest = await _streamRepository.GetAsync(stream.Id, cancellationToken: cancellationToken);
                if (string.IsNullOrWhiteSpace(latest.SchemaBaselineJson))
                {
                    latest.UpdateSchemaBaseline(eval.CurrentHash, eval.CurrentJson);
                    await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
                }

                await initUow.CompleteAsync(cancellationToken);
                return latest;
            }

            return stream;
        }

        if (!eval.HasDrift || stream.SchemaDriftPolicy == CdcSchemaDriftPolicy.Ignore)
        {
            return stream;
        }

        var pauseStream = stream.SchemaDriftPolicy == CdcSchemaDriftPolicy.PauseAndAlert;
        var summaryChanged = !stream.SchemaDriftDetected ||
                             !string.Equals(stream.SchemaDriftSummary, eval.Summary, StringComparison.Ordinal);

        CdcStreamDefinition updated;
        using (var driftUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            updated = await _streamRepository.GetAsync(stream.Id, cancellationToken: cancellationToken);
            updated.MarkSchemaDriftDetected(eval.Summary, DateTime.UtcNow, pauseStream);
            await _streamRepository.UpdateAsync(updated, autoSave: true, cancellationToken: cancellationToken);
            await driftUow.CompleteAsync(cancellationToken);
        }

        if (pauseStream)
        {
            await _scheduleSynchronizer.SyncAsync(stream.Id, CdcStreamStatus.Paused, stream.PollIntervalSeconds, cancellationToken);
            PlatformMetrics.CdcSchemaDriftEventsTotal.WithLabels("paused").Inc();
            await TrySendDriftAlertAsync(updated, eval.Summary, paused: true, cancellationToken);
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamSchemaDriftDetected)
                .WithData("Reason", eval.Summary);
        }

        if (summaryChanged)
        {
            PlatformMetrics.CdcSchemaDriftEventsTotal.WithLabels("warn").Inc();
            await TrySendDriftAlertAsync(updated, eval.Summary, paused: false, cancellationToken);
        }

        return updated;
    }

    private async Task TrySendDriftAlertAsync(
        CdcStreamDefinition stream,
        string summary,
        bool paused,
        CancellationToken cancellationToken)
    {
        if (_notificationService == null)
        {
            return;
        }

        try
        {
            await _notificationService.SendAsync(
                new NotificationSendRequest
                {
                    Title = paused
                        ? $"CDC 流「{stream.Name}」上游 Schema 漂移已自动暂停"
                        : $"CDC 流「{stream.Name}」检测到上游 Schema 漂移",
                    Content = $"上游表结构变更摘要：{summary}\n请核对下游目标结构并在 CDC 控制台确认新 Schema。",
                    Level = paused ? NotificationLevel.Warning : NotificationLevel.Info,
                    Source = NotificationSources.DataIntegration,
                    ActionPath = "/data-integration/cdc",
                    ActionLabel = "查看 CDC 流",
                },
                cancellationToken);
        }
        catch (Exception notifyEx)
        {
            _logger.LogWarning(
                "CDC Schema 漂移告警发送失败 streamId={StreamId} exceptionType={Type}",
                stream.Id,
                notifyEx.GetType().FullName ?? notifyEx.GetType().Name);
        }
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

    private Task<PgOutputSnapshotBatchResult> DispatchSnapshotAsync(
        DataSource source,
        CdcStreamDefinition stream,
        DataSource? targetSink,
        CancellationToken cancellationToken)
        => stream.SourceType switch
        {
            CdcSourceType.SqlServerChangeTracking => RequireSqlServerEngine().SnapshotAsync(source, stream, targetSink, cancellationToken),
            CdcSourceType.MySqlBinlog => RequireMySqlEngine().SnapshotAsync(source, stream, targetSink, cancellationToken),
            _ => _captureService.SnapshotAsync(source, stream, targetSink, cancellationToken),
        };

    private Task<PgOutputCaptureBatchResult> DispatchCaptureAsync(
        DataSource source,
        CdcStreamDefinition stream,
        DataSource? targetSink,
        CancellationToken cancellationToken)
    {
        Task OnSchemaAsync(CdcSchemaDriftEvaluation eval)
            => HandleSchemaEvaluationAsync(stream, eval, cancellationToken).ContinueWith(
                t => { stream = t.Result; },
                cancellationToken,
                TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        return stream.SourceType switch
        {
            CdcSourceType.SqlServerChangeTracking => RequireSqlServerEngine().CaptureBatchAsync(
                source, stream, targetSink, cancellationToken, OnSchemaAsync),
            CdcSourceType.MySqlBinlog => RequireMySqlEngine().CaptureBatchAsync(
                source, stream, targetSink, cancellationToken, OnSchemaAsync),
            _ => _captureService.CaptureBatchAsync(
                source, stream, targetSink, cancellationToken, OnSchemaAsync),
        };
    }

    private SqlServerCdcCaptureService RequireSqlServerEngine()
        => _sqlServerCaptureService ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
            .WithData("Reason", "SQL Server Change Tracking 捕获引擎未启用。");

    private MySqlBinlogCdcCaptureService RequireMySqlEngine()
        => _mySqlCaptureService ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
            .WithData("Reason", "MySQL Binlog 捕获引擎未启用。");
}
