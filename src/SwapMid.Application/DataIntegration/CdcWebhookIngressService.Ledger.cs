using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SwapMid.Monitoring;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

public sealed partial class CdcWebhookIngressService
{
    private async Task<(CdcStreamDefinition? Stream, DataSource? Source, DataSource? TargetSink)> LoadStreamAndBindingsAsync(
        Guid streamId,
        CancellationToken cancellationToken)
    {
        using var readUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var stream = await _streamRepository.FindAsync(streamId, cancellationToken: cancellationToken);
        if (stream == null)
        {
            await readUow.CompleteAsync(cancellationToken);
            return (null, null, null);
        }

        var source = await _sourceRepository.FindAsync(stream.SourceId, cancellationToken: cancellationToken);
        DataSource? targetSink = null;
        if (stream.TargetMode is (CdcTargetMode.ExternalSink or CdcTargetMode.RelationalTable) &&
            stream.TargetSinkId.HasValue)
        {
            targetSink = await _sourceRepository.FindAsync(stream.TargetSinkId.Value, cancellationToken: cancellationToken);
        }

        await readUow.CompleteAsync(cancellationToken);
        return (stream, source, targetSink);
    }

    private async Task<CdcStreamDefinition> CommitSuccessAndInferredSchemaAsync(
        Guid streamId,
        string newCheckpoint,
        long acceptedCount,
        CdcSchemaDriftEvaluation schemaEval,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        using var writeUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var latest = await _streamRepository.GetAsync(streamId, cancellationToken: cancellationToken);
        latest.MarkCaptureSucceeded(newCheckpoint, acceptedCount, nowUtc);
        if (string.IsNullOrWhiteSpace(latest.SchemaBaselineJson) && schemaEval.CurrentJson != "[]")
        {
            latest.UpdateSchemaBaseline(schemaEval.CurrentHash, schemaEval.CurrentJson);
        }
        else if (schemaEval.HasDrift && latest.SchemaDriftPolicy == CdcSchemaDriftPolicy.Warn)
        {
            latest.MarkSchemaDriftDetected(schemaEval.Summary, nowUtc, pauseStream: false);
            PlatformMetrics.CdcSchemaDriftEventsTotal.WithLabels("warn").Inc();
        }

        await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
        await writeUow.CompleteAsync(cancellationToken);
        return latest;
    }

    private async Task PersistSchemaDriftPauseAsync(
        Guid streamId,
        string summary,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        using var driftUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var latest = await _streamRepository.FindAsync(streamId, cancellationToken: cancellationToken);
        if (latest != null)
        {
            latest.MarkSchemaDriftDetected(summary, nowUtc, pauseStream: true);
            await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
        }

        await driftUow.CompleteAsync(cancellationToken);
    }

    private async Task PersistCaptureFailureAndLedgerAsync(
        Guid streamId,
        string deliveryId,
        PipelineExecutionFailurePolicy.FailureDescriptor failure,
        int payloadSizeBytes,
        string? boundedReplayPayload,
        DateTime nowUtc,
        bool isReplay,
        CdcWebhookDeliveryRecord? replayRecord,
        CancellationToken cancellationToken)
    {
        using (var errUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var latest = await _streamRepository.FindAsync(streamId, cancellationToken: cancellationToken);
            if (latest != null)
            {
                latest.MarkCaptureFailed(failure.ErrorCode, failure.ExceptionType);
                await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
            }

            await errUow.CompleteAsync(cancellationToken);
        }

        if (!isReplay)
        {
            await TryRecordDeliveryAsync(
                streamId, deliveryId, CdcWebhookIngestOutcome.Failed, 500, 0, 0, null,
                failure.ErrorCode, failure.ExceptionType, payloadSizeBytes, boundedReplayPayload, nowUtc, cancellationToken);
        }
        else if (replayRecord != null)
        {
            await UpdateReplayRecordFailureAsync(replayRecord.Id, failure.ErrorCode, failure.ExceptionType, nowUtc, cancellationToken);
        }
    }

    private async Task TryRecordDeliveryAsync(
        Guid streamId,
        string deliveryId,
        CdcWebhookIngestOutcome outcome,
        int httpStatusCode,
        long acceptedEventCount,
        long deliveredCount,
        string? checkpointLsn,
        string? errorCode,
        string? errorSummary,
        int payloadSizeBytes,
        string? storedPayloadJson,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        if (_deliveryRepository == null)
        {
            return;
        }

        try
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var record = new CdcWebhookDeliveryRecord(
                Guid.NewGuid(),
                streamId,
                deliveryId,
                (int)outcome,
                httpStatusCode,
                acceptedEventCount,
                deliveredCount,
                checkpointLsn,
                errorCode,
                errorSummary,
                payloadSizeBytes,
                storedPayloadJson,
                receivedAtUtc);
            await _deliveryRepository.InsertAsync(record, autoSave: true, cancellationToken: cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Push Webhook 投递台账写入失败 streamId={StreamId} deliveryId={DeliveryId} exceptionType={Type}",
                streamId, deliveryId, ex.GetType().FullName ?? ex.GetType().Name);
        }
    }

    private async Task UpdateReplayRecordSuccessAsync(
        Guid recordId,
        long acceptedEventCount,
        long deliveredCount,
        string? checkpointLsn,
        DateTime replayedAtUtc,
        CancellationToken cancellationToken)
    {
        if (_deliveryRepository == null) return;
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var rec = await _deliveryRepository.FindAsync(recordId, cancellationToken: cancellationToken);
        if (rec != null)
        {
            rec.MarkReplaySucceeded(acceptedEventCount, deliveredCount, checkpointLsn, replayedAtUtc);
            await _deliveryRepository.UpdateAsync(rec, autoSave: true, cancellationToken: cancellationToken);
        }

        await uow.CompleteAsync(cancellationToken);
    }

    private async Task UpdateReplayRecordFailureAsync(
        Guid recordId,
        string errorCode,
        string errorSummary,
        DateTime attemptedAtUtc,
        CancellationToken cancellationToken)
    {
        if (_deliveryRepository == null) return;
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var rec = await _deliveryRepository.FindAsync(recordId, cancellationToken: cancellationToken);
        if (rec != null)
        {
            rec.MarkReplayFailed(errorCode, errorSummary, attemptedAtUtc);
            await _deliveryRepository.UpdateAsync(rec, autoSave: true, cancellationToken: cancellationToken);
        }

        await uow.CompleteAsync(cancellationToken);
    }

    private static string? ExtractBoundedReplayPayload(byte[] rawBodyBytes)
    {
        if (rawBodyBytes.Length == 0 || rawBodyBytes.Length > CdcWebhookDeliveryConsts.MaxStoredPayloadBytes)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(rawBodyBytes);
        return text.Length <= CdcWebhookDeliveryConsts.MaxStoredPayloadLength ? text : null;
    }

    private static CdcWebhookDeliveryRecordDto MapDeliveryDto(CdcWebhookDeliveryRecord r) => new()
    {
        Id = r.Id,
        StreamId = r.StreamId,
        DeliveryId = r.DeliveryId,
        Outcome = Enum.IsDefined(typeof(CdcWebhookIngestOutcome), r.Outcome)
            ? (CdcWebhookIngestOutcome)r.Outcome
            : CdcWebhookIngestOutcome.Failed,
        HttpStatusCode = r.HttpStatusCode,
        AcceptedEventCount = r.AcceptedEventCount,
        DeliveredCount = r.DeliveredCount,
        CheckpointLsn = r.CheckpointLsn,
        ErrorCode = r.ErrorCode,
        ErrorSummary = r.ErrorSummary,
        PayloadSizeBytes = r.PayloadSizeBytes,
        CanReplay = r.CanReplay,
        ReceivedAtUtc = r.ReceivedAtUtc,
        ReplayedAtUtc = r.ReplayedAtUtc,
        ReplayCount = r.ReplayCount,
    };
}
