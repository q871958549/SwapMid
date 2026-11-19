using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SwapMid.Monitoring;
using SwapMid.Permissions;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

/// <summary>
/// Serverless 认证 Push Webhook CDC 接入、自动 Schema 推导与死信重放服务：
/// 使用常时间 HMAC-SHA256（<c>X-SwapMid-Signature: sha256=&lt;hex&gt;</c>）校验绑定 RestApi 数据源中的加密密钥，
/// 支持单事件或数组批次推送、幂等 <c>X-SwapMid-Delivery-Id</c>、自动基线推导/漂移护栏、单调 <c>webhook:{seq}</c> 检查点及死信一键重放。
/// </summary>
public sealed partial class CdcWebhookIngressService : ICdcWebhookIngressService, ITransientDependency
{
    public const int MaxPayloadBytes = 2 * 1024 * 1024;
    public const int MinWebhookSecretLength = 16;
    public const string SignatureHeaderName = "X-SwapMid-Signature";
    public const string DeliveryIdHeaderName = "X-SwapMid-Delivery-Id";

    private static readonly TimeSpan LockAcquireTimeout = TimeSpan.FromSeconds(5);

    private readonly ICdcStreamRepository _streamRepository;
    private readonly IRepository<DataSource, Guid> _sourceRepository;
    private readonly IIntegrationEventPublisher _integrationEventPublisher;
    private readonly DataPipelineExecutionCoordinator _executionCoordinator;
    private readonly IReadOnlyDictionary<DataSourceType, IEtlSinkWriter> _sinkWriters;
    private readonly IEtlRelationalConnectionFactory _connectionFactory;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly PipelineExecutionFailurePolicy _failurePolicy;
    private readonly ILogger<CdcWebhookIngressService> _logger;
    private readonly CdcObjectStorageCompactionService? _lakehouseCompactor;
    private readonly ExternalLakehouseCatalogSyncService? _catalogSyncService;
    private readonly CdcDataContractEvaluator? _contractEvaluator;
    private readonly IRepository<CdcWebhookDeliveryRecord, Guid>? _deliveryRepository;
    private readonly ICurrentTenant? _currentTenant;

    public CdcWebhookIngressService(
        ICdcStreamRepository streamRepository,
        IRepository<DataSource, Guid> sourceRepository,
        IIntegrationEventPublisher integrationEventPublisher,
        DataPipelineExecutionCoordinator executionCoordinator,
        IEtlRelationalConnectionFactory connectionFactory,
        IAbpDistributedLock distributedLock,
        IUnitOfWorkManager unitOfWorkManager,
        PipelineExecutionFailurePolicy failurePolicy,
        ILogger<CdcWebhookIngressService> logger,
        IEnumerable<IEtlSinkWriter>? sinkWriters = null,
        CdcObjectStorageCompactionService? lakehouseCompactor = null,
        ExternalLakehouseCatalogSyncService? catalogSyncService = null,
        CdcDataContractEvaluator? contractEvaluator = null,
        IRepository<CdcWebhookDeliveryRecord, Guid>? deliveryRepository = null,
        ICurrentTenant? currentTenant = null)
    {
        _streamRepository = streamRepository;
        _sourceRepository = sourceRepository;
        _integrationEventPublisher = integrationEventPublisher;
        _executionCoordinator = executionCoordinator;
        _connectionFactory = connectionFactory;
        _distributedLock = distributedLock;
        _unitOfWorkManager = unitOfWorkManager;
        _failurePolicy = failurePolicy;
        _logger = logger;
        _sinkWriters = (sinkWriters ?? Array.Empty<IEtlSinkWriter>()).ToDictionary(x => x.TargetType);
        _lakehouseCompactor = lakehouseCompactor;
        _catalogSyncService = catalogSyncService;
        _contractEvaluator = contractEvaluator;
        _deliveryRepository = deliveryRepository;
        _currentTenant = currentTenant;
    }

    public void RecordPayloadTooLarge()
        => PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("rejected_payload").Inc();

    public async Task<CdcWebhookIngestExecutionResult> IngestAsync(
        Guid streamId,
        byte[] rawBodyBytes,
        string? signatureHeader,
        string? deliveryIdHeader,
        CancellationToken cancellationToken = default)
    {
        if (rawBodyBytes.Length == 0)
        {
            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("rejected_payload").Inc();
            return new(CdcWebhookIngestOutcome.InvalidPayload, SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid, "Webhook 请求体不能为空。");
        }

        if (rawBodyBytes.Length > MaxPayloadBytes)
        {
            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("rejected_payload").Inc();
            return new(CdcWebhookIngestOutcome.PayloadTooLarge, SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid, $"Webhook 请求体超过最大限制（{MaxPayloadBytes / (1024 * 1024)} MiB）。");
        }

        await using var lockHandle = await _distributedLock.TryAcquireAsync(
            CdcStreamCaptureCoordinator.DistributedLockPrefix + streamId.ToString("N"),
            LockAcquireTimeout,
            cancellationToken);
        if (lockHandle == null)
        {
            return new(CdcWebhookIngestOutcome.Busy, SwapMidDomainErrorCodes.DataIntegration.CdcStreamCaptureBusy, "当前 CDC 流正在处理上一批推送，请稍后重试。");
        }

        var (stream, source, targetSink) = await LoadStreamAndBindingsAsync(streamId, cancellationToken);
        if (stream == null || stream.SourceType != CdcSourceType.PushWebhook)
        {
            return new(CdcWebhookIngestOutcome.NotFound, SwapMidDomainErrorCodes.DataIntegration.CdcStreamNotFound, "未找到对应的 Push Webhook CDC 流。");
        }

        var deliveryId = ResolveDeliveryId(deliveryIdHeader, rawBodyBytes);
        var nowUtc = DateTime.UtcNow;

        // Fail-closed HMAC authentication BEFORE stream status check or JSON body parsing; NEVER persist attacker payload bytes.
        if (source == null ||
            source.Type != DataSourceType.RestApi ||
            !TryExtractWebhookSecret(source.Config, out var secret) ||
            !VerifySignature(secret, rawBodyBytes, signatureHeader))
        {
            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("rejected_auth").Inc();
            await TryRecordDeliveryAsync(
                stream.Id, deliveryId, CdcWebhookIngestOutcome.Unauthorized, 401, 0, 0, null,
                "SwapMid:ETL:WebhookSignatureInvalid", "Webhook HMAC-SHA256 签名校验失败或签名头缺失。",
                rawBodyBytes.Length, storedPayloadJson: null, nowUtc, cancellationToken);
            return new(CdcWebhookIngestOutcome.Unauthorized, "SwapMid:ETL:WebhookSignatureInvalid", "Webhook HMAC-SHA256 签名校验失败或签名头缺失。");
        }

        var boundedReplayPayload = ExtractBoundedReplayPayload(rawBodyBytes);
        if (stream.Status != CdcStreamStatus.Active)
        {
            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("rejected_inactive").Inc();
            await TryRecordDeliveryAsync(
                stream.Id, deliveryId, CdcWebhookIngestOutcome.Inactive, 409, 0, 0, stream.CheckpointLsn,
                SwapMidDomainErrorCodes.DataIntegration.CdcStreamNotActive, "Push Webhook CDC 流未处于激活状态（已保留待重放载荷）。",
                rawBodyBytes.Length, boundedReplayPayload, nowUtc, cancellationToken);
            return new(CdcWebhookIngestOutcome.Inactive, SwapMidDomainErrorCodes.DataIntegration.CdcStreamNotActive, "当前 Push Webhook CDC 流未处于激活状态，请先激活后再推送事件。");
        }

        return await ProcessAuthenticatedPayloadAsync(
            stream,
            targetSink,
            rawBodyBytes,
            boundedReplayPayload,
            deliveryId,
            nowUtc,
            isReplay: false,
            replayRecord: null,
            cancellationToken);
    }

    public async Task<CdcWebhookDeliveryListDto> GetDeliveriesAsync(
        Guid streamId,
        int maxCount = 30,
        CancellationToken cancellationToken = default)
    {
        PipelineExecutionAppService.EnsureHostContext(
            _currentTenant?.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Default);

        var stream = await _streamRepository.FindAsync(streamId, cancellationToken: cancellationToken);
        if (stream == null || stream.SourceType != CdcSourceType.PushWebhook)
        {
            throw new EntityNotFoundException(typeof(CdcStreamDefinition), streamId);
        }

        if (_deliveryRepository == null)
        {
            return new CdcWebhookDeliveryListDto { StreamId = streamId };
        }

        var limit = Math.Clamp(maxCount <= 0 ? 30 : maxCount, 1, 100);
        var all = await _deliveryRepository.GetListAsync(x => x.StreamId == streamId, cancellationToken: cancellationToken);
        var ordered = all
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Take(limit)
            .Select(MapDeliveryDto)
            .ToList();
        return new CdcWebhookDeliveryListDto
        {
            StreamId = streamId,
            TotalCount = all.Count,
            ReplayableCount = all.Count(x => x.CanReplay),
            Items = ordered,
        };
    }

    public async Task<CdcWebhookIngestResultDto> ReplayDeliveryAsync(
        Guid streamId,
        Guid deliveryRecordId,
        CancellationToken cancellationToken = default)
    {
        PipelineExecutionAppService.EnsureHostContext(
            _currentTenant?.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage);

        if (_deliveryRepository == null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "Webhook 投递台账仓储未启用。");
        }

        await using var lockHandle = await _distributedLock.TryAcquireAsync(
            CdcStreamCaptureCoordinator.DistributedLockPrefix + streamId.ToString("N"),
            LockAcquireTimeout,
            cancellationToken);
        if (lockHandle == null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamCaptureBusy);
        }

        var (stream, _, targetSink) = await LoadStreamAndBindingsAsync(streamId, cancellationToken);
        if (stream == null || stream.SourceType != CdcSourceType.PushWebhook)
        {
            throw new EntityNotFoundException(typeof(CdcStreamDefinition), streamId);
        }

        if (stream.Status != CdcStreamStatus.Active)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamNotActive)
                .WithData("Reason", "重放 Push Webhook 死信批次前请先激活 CDC 流。");
        }

        CdcWebhookDeliveryRecord? record;
        using (var readUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            record = await _deliveryRepository.FindAsync(deliveryRecordId, cancellationToken: cancellationToken);
            await readUow.CompleteAsync(cancellationToken);
        }

        if (record == null || record.StreamId != streamId)
        {
            throw new EntityNotFoundException(typeof(CdcWebhookDeliveryRecord), deliveryRecordId);
        }

        if (!record.CanReplay || string.IsNullOrWhiteSpace(record.StoredPayloadJson))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "该 Webhook 投递记录不可重放（已成功投递、未通过验签或超过 64 KiB 缓存上限）。");
        }

        var rawBytes = Encoding.UTF8.GetBytes(record.StoredPayloadJson);
        var execution = await ProcessAuthenticatedPayloadAsync(
            stream,
            targetSink,
            rawBytes,
            record.StoredPayloadJson,
            record.DeliveryId,
            DateTime.UtcNow,
            isReplay: true,
            replayRecord: record,
            cancellationToken);

        if (execution.Outcome != CdcWebhookIngestOutcome.Accepted || execution.Result == null)
        {
            throw new BusinessException(execution.ErrorCode)
                .WithData("Reason", execution.ErrorMessage);
        }

        return execution.Result;
    }

    private async Task<CdcWebhookIngestExecutionResult> ProcessAuthenticatedPayloadAsync(
        CdcStreamDefinition stream,
        DataSource? targetSink,
        byte[] rawBodyBytes,
        string? boundedReplayPayload,
        string deliveryId,
        DateTime nowUtc,
        bool isReplay,
        CdcWebhookDeliveryRecord? replayRecord,
        CancellationToken cancellationToken)
    {
        var baseSeq = ParseWebhookSequence(stream.CheckpointLsn);
        if (!TryParsePayloadEvents(rawBodyBytes, stream, baseSeq, nowUtc, out var parsedEvents, out var parseError))
        {
            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("rejected_payload").Inc();
            if (!isReplay)
            {
                await TryRecordDeliveryAsync(
                    stream.Id, deliveryId, CdcWebhookIngestOutcome.InvalidPayload, 400, 0, 0, stream.CheckpointLsn,
                    SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid, parseError,
                    rawBodyBytes.Length, storedPayloadJson: null, nowUtc, cancellationToken);
            }

            return new(CdcWebhookIngestOutcome.InvalidPayload, SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid, parseError);
        }

        var monitored = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables, defaultSchema: "webhook");
        var matched = monitored.Count > 0
            ? parsedEvents.Where(e => CdcProtocolHelpers.ShouldCaptureRelation(e.Schema, e.Table, monitored)).ToList()
            : parsedEvents;

        if (matched.Count == 0)
        {
            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels(isReplay ? "replayed" : "accepted").Inc();
            if (!isReplay)
            {
                await TryRecordDeliveryAsync(
                    stream.Id, deliveryId, CdcWebhookIngestOutcome.Accepted, 200, 0, 0, stream.CheckpointLsn,
                    null, null, rawBodyBytes.Length, null, nowUtc, cancellationToken);
            }
            else if (replayRecord != null)
            {
                await UpdateReplayRecordSuccessAsync(replayRecord.Id, 0, 0, stream.CheckpointLsn, nowUtc, cancellationToken);
            }

            return new(
                CdcWebhookIngestOutcome.Accepted,
                string.Empty,
                string.Empty,
                new CdcWebhookIngestResultDto
                {
                    StreamId = stream.Id,
                    DeliveryId = deliveryId,
                    AcceptedEventCount = 0,
                    DeliveredCount = 0,
                    CheckpointLsn = stream.CheckpointLsn,
                    ReceivedAtUtc = nowUtc,
                    Message = $"已验证签名并接收批次 {deliveryId}（全部 {parsedEvents.Count} 条事件被监听表过滤规则跳过）。",
                });
        }

        var inferredSchemas = CdcWebhookSchemaInferenceHelper.InferSchemas(matched);
        var schemaEval = CdcSchemaDriftEvaluator.Compare(stream.SchemaBaselineJson, inferredSchemas);
        if (!string.IsNullOrWhiteSpace(stream.SchemaBaselineJson) &&
            schemaEval.HasDrift &&
            stream.SchemaDriftPolicy == CdcSchemaDriftPolicy.PauseAndAlert)
        {
            await PersistSchemaDriftPauseAsync(stream.Id, schemaEval.Summary, nowUtc, cancellationToken);
            PlatformMetrics.CdcSchemaDriftEventsTotal.WithLabels("paused").Inc();
            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("failed").Inc();
            if (!isReplay)
            {
                await TryRecordDeliveryAsync(
                    stream.Id, deliveryId, CdcWebhookIngestOutcome.Inactive, 409, 0, 0, stream.CheckpointLsn,
                    SwapMidDomainErrorCodes.DataIntegration.CdcStreamSchemaDriftDetected, schemaEval.Summary,
                    rawBodyBytes.Length, boundedReplayPayload, nowUtc, cancellationToken);
            }
            else if (replayRecord != null)
            {
                await UpdateReplayRecordFailureAsync(
                    replayRecord.Id, SwapMidDomainErrorCodes.DataIntegration.CdcStreamSchemaDriftDetected, schemaEval.Summary, nowUtc, cancellationToken);
            }

            return new(
                CdcWebhookIngestOutcome.Inactive,
                SwapMidDomainErrorCodes.DataIntegration.CdcStreamSchemaDriftDetected,
                $"检测到 Push Webhook 载荷 Schema 漂移，流已自动暂停（批次已存入重放台账）：{schemaEval.Summary}");
        }

        var newCheckpoint = $"webhook:{(baseSeq + matched.Count).ToString(CultureInfo.InvariantCulture)}";
        var idempotencyKey = $"webhook:{deliveryId}";

        try
        {
            var delivered = await CdcStreamDeliveryDispatcher.DeliverAsync(
                stream,
                targetSink,
                matched,
                batchEndLsn: null,
                _integrationEventPublisher,
                _executionCoordinator,
                _sinkWriters,
                _connectionFactory,
                _logger,
                cancellationToken,
                eventSequenceOffset: baseSeq,
                idempotencyKeyOverride: idempotencyKey);

            var committedStream = await CommitSuccessAndInferredSchemaAsync(
                stream.Id, newCheckpoint, matched.Count, schemaEval, nowUtc, cancellationToken);

            foreach (var ev in matched)
            {
                var opLabel = ev.Operation is "insert" or "update" or "delete" ? ev.Operation : "insert";
                PlatformMetrics.CdcEventsCapturedTotal.WithLabels(opLabel).Inc();
            }

            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels(isReplay ? "replayed" : "accepted").Inc();

            if (!isReplay)
            {
                await TryRecordDeliveryAsync(
                    stream.Id, deliveryId, CdcWebhookIngestOutcome.Accepted, 200, matched.Count, delivered, newCheckpoint,
                    null, null, rawBodyBytes.Length, storedPayloadJson: null, nowUtc, cancellationToken);
            }
            else if (replayRecord != null)
            {
                await UpdateReplayRecordSuccessAsync(replayRecord.Id, matched.Count, delivered, newCheckpoint, nowUtc, cancellationToken);
            }

            if (targetSink != null && _lakehouseCompactor != null)
            {
                await _lakehouseCompactor.TryAutoCompactAsync(committedStream, targetSink, cancellationToken);
            }

            if (targetSink != null && _catalogSyncService != null && delivered > 0)
            {
                await _catalogSyncService.TryBestEffortSyncAsync(committedStream, targetSink, versionOverride: null, cancellationToken);
            }

            if (_contractEvaluator != null)
            {
                await _contractEvaluator.TryEvaluateAfterCaptureAsync(committedStream, walLagBytes: null, cancellationToken);
            }

            return new(
                CdcWebhookIngestOutcome.Accepted,
                string.Empty,
                string.Empty,
                new CdcWebhookIngestResultDto
                {
                    StreamId = stream.Id,
                    DeliveryId = deliveryId,
                    AcceptedEventCount = matched.Count,
                    DeliveredCount = delivered,
                    CheckpointLsn = newCheckpoint,
                    ReceivedAtUtc = nowUtc,
                    Message = isReplay
                        ? $"已重放批次 {deliveryId} 并投递 {matched.Count} 条事件（投递计数 {delivered}，位点 {newCheckpoint}）。"
                        : $"已验签接入并投递 {matched.Count} 条事件（投递计数 {delivered}，位点 {newCheckpoint}）。",
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("failed").Inc();
            var failure = _failurePolicy.DescribeFailure(ex, stream.Id);
            await PersistCaptureFailureAndLedgerAsync(
                stream.Id, deliveryId, failure, rawBodyBytes.Length, boundedReplayPayload, nowUtc, isReplay, replayRecord, cancellationToken);
            _logger.LogWarning(
                "Push Webhook CDC 投递失败 streamId={StreamId} deliveryId={DeliveryId} code={Code} exceptionType={Type}",
                stream.Id, deliveryId, failure.ErrorCode, failure.ExceptionType);
            throw;
        }
    }
}
