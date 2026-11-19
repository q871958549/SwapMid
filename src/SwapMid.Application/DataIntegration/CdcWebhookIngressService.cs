using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SwapMid.Monitoring;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

/// <summary>
/// Serverless 认证 Push Webhook CDC 接入服务：
/// 使用常时间 HMAC-SHA256（<c>X-SwapMid-Signature: sha256=&lt;hex&gt;</c>）校验绑定 RestApi 数据源中的加密密钥，
/// 支持单事件或数组批次推送、幂等 <c>X-SwapMid-Delivery-Id</c>、单调 <c>webhook:{seq}</c> 检查点及全下游投递（含 Delta Lake / Apache Iceberg v2）。
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
        CdcDataContractEvaluator? contractEvaluator = null)
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

        CdcStreamDefinition? stream;
        DataSource? source;
        DataSource? targetSink = null;
        using (var readUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            stream = await _streamRepository.FindAsync(streamId, cancellationToken: cancellationToken);
            if (stream == null || stream.SourceType != CdcSourceType.PushWebhook)
            {
                await readUow.CompleteAsync(cancellationToken);
                return new(CdcWebhookIngestOutcome.NotFound, SwapMidDomainErrorCodes.DataIntegration.CdcStreamNotFound, "未找到对应的 Push Webhook CDC 流。");
            }

            source = await _sourceRepository.FindAsync(stream.SourceId, cancellationToken: cancellationToken);
            if (stream.TargetMode is (CdcTargetMode.ExternalSink or CdcTargetMode.RelationalTable) &&
                stream.TargetSinkId.HasValue)
            {
                targetSink = await _sourceRepository.FindAsync(stream.TargetSinkId.Value, cancellationToken: cancellationToken);
            }

            await readUow.CompleteAsync(cancellationToken);
        }

        // Fail-closed HMAC authentication BEFORE stream status check or JSON body parsing.
        if (source == null ||
            source.Type != DataSourceType.RestApi ||
            !TryExtractWebhookSecret(source.Config, out var secret) ||
            !VerifySignature(secret, rawBodyBytes, signatureHeader))
        {
            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("rejected_auth").Inc();
            return new(CdcWebhookIngestOutcome.Unauthorized, "SwapMid:ETL:WebhookSignatureInvalid", "Webhook HMAC-SHA256 签名校验失败或签名头缺失。");
        }

        if (stream.Status != CdcStreamStatus.Active)
        {
            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("rejected_inactive").Inc();
            return new(CdcWebhookIngestOutcome.Inactive, SwapMidDomainErrorCodes.DataIntegration.CdcStreamNotActive, "当前 Push Webhook CDC 流未处于激活状态，请先激活后再推送事件。");
        }

        var nowUtc = DateTime.UtcNow;
        var baseSeq = ParseWebhookSequence(stream.CheckpointLsn);
        if (!TryParsePayloadEvents(rawBodyBytes, stream, baseSeq, nowUtc, out var parsedEvents, out var parseError))
        {
            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("rejected_payload").Inc();
            return new(CdcWebhookIngestOutcome.InvalidPayload, SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid, parseError);
        }

        var deliveryId = ResolveDeliveryId(deliveryIdHeader, rawBodyBytes);
        var monitored = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables, defaultSchema: "webhook");
        var matched = monitored.Count > 0
            ? parsedEvents.Where(e => CdcProtocolHelpers.ShouldCaptureRelation(e.Schema, e.Table, monitored)).ToList()
            : parsedEvents;

        if (matched.Count == 0)
        {
            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("accepted").Inc();
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

            CdcStreamDefinition committedStream;
            using (var writeUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                var latest = await _streamRepository.GetAsync(stream.Id, cancellationToken: cancellationToken);
                latest.MarkCaptureSucceeded(newCheckpoint, matched.Count, nowUtc);
                await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
                await writeUow.CompleteAsync(cancellationToken);
                committedStream = latest;
            }

            foreach (var ev in matched)
            {
                var opLabel = ev.Operation is "insert" or "update" or "delete" ? ev.Operation : "insert";
                PlatformMetrics.CdcEventsCapturedTotal.WithLabels(opLabel).Inc();
            }

            PlatformMetrics.CdcWebhookIngestsTotal.WithLabels("accepted").Inc();

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
                    Message = $"已验签接入并投递 {matched.Count} 条事件（投递计数 {delivered}，位点 {newCheckpoint}）。",
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
            using (var errUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                var latest = await _streamRepository.FindAsync(stream.Id, cancellationToken: cancellationToken);
                if (latest != null)
                {
                    latest.MarkCaptureFailed(failure.ErrorCode, failure.ExceptionType);
                    await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
                }

                await errUow.CompleteAsync(cancellationToken);
            }

            _logger.LogWarning(
                "Push Webhook CDC 投递失败 streamId={StreamId} deliveryId={DeliveryId} code={Code} exceptionType={Type}",
                stream.Id, deliveryId, failure.ErrorCode, failure.ExceptionType);
            throw;
        }
    }
}
