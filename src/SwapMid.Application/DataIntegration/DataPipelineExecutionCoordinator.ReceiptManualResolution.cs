using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;

namespace SwapMid.DataIntegration;

public sealed partial class DataPipelineExecutionCoordinator
{
    public const string ReceiptManualOverrideSourcePropertyName =
        "PipelineExecution.ReceiptManualOverrideSource";
    public const string ReceiptAbsenceVerifiedAtPropertyName =
        "PipelineExecution.ReceiptAbsenceVerifiedAt";

    private const string ReceiptManualOverrideSource =
        "target-receipt-v1-absent-break-glass";

    private async Task<IEtlTargetReceiptReadFence?>
        AcquireReceiptAbsenceFenceForManualResolutionAsync(
        ResolutionPreparation preparation,
        CancellationToken cancellationToken)
    {
        if (preparation.ReceiptContext == null)
        {
            return null;
        }

        if (preparation.ReceiptTarget == null)
        {
            throw ReceiptInconclusive();
        }

        IEtlTargetReceiptReadFence readFence;
        try
        {
            readFence = await _targetReceiptStore.AcquireReadFenceAsync(
                preparation.ReceiptTarget,
                preparation.ReceiptContext,
                cancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
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
                "ETL 人工未应用裁决前目标回执暂不可核验 executionId={ExecutionId} exceptionType={ExceptionType}",
                preparation.Execution.Id,
                targetException.GetType().FullName ??
                targetException.GetType().Name);
            throw ReceiptUnavailable();
        }

        var receipt = readFence.Receipt;
        if (receipt == null)
        {
            RecordCoordinationEvent("target_receipt_absent");
            return readFence;
        }

        if (ReceiptMatchesExecution(
                preparation.Execution,
                preparation.ReceiptContext,
                receipt))
        {
            RecordCoordinationEvent(
                "target_receipt_manual_override_blocked");
            _logger.LogCritical(
                "ETL 已存在完整目标回执，阻止人工未应用裁决 executionId={ExecutionId}",
                preparation.Execution.Id);
            await DisposeReceiptReadFenceNoThrowAsync(
                readFence,
                preparation.Execution.Id);
            throw ReceiptVerificationRequired();
        }

        RecordCoordinationEvent("target_receipt_mismatch");
        _logger.LogCritical(
            "ETL 人工未应用裁决前发现不匹配目标回执 executionId={ExecutionId}",
            preparation.Execution.Id);
        await DisposeReceiptReadFenceNoThrowAsync(
            readFence,
            preparation.Execution.Id);
        throw ReceiptInconclusive();
    }

    private async Task DisposeReceiptReadFenceNoThrowAsync(
        IEtlTargetReceiptReadFence readFence,
        Guid executionId)
    {
        try
        {
            await readFence.DisposeAsync();
        }
        catch (Exception releaseException)
        {
            _logger.LogWarning(
                "ETL 人工裁决目标写锁释放失败 executionId={ExecutionId} exceptionType={ExceptionType}",
                executionId,
                releaseException.GetType().FullName ??
                releaseException.GetType().Name);
        }
    }

    private static void StoreReceiptManualNotAppliedAudit(
        PipelineExecution execution,
        DateTime resolvedAt)
    {
        execution.SetProperty(
            ReceiptManualOverrideSourcePropertyName,
            ReceiptManualOverrideSource);
        execution.SetProperty(
            ReceiptAbsenceVerifiedAtPropertyName,
            resolvedAt.ToString("O"));
    }

    private static bool HasCompleteReceiptManualNotAppliedAudit(
        PipelineExecution execution)
    {
        if (execution.EvidenceProtocol != 1)
        {
            return true;
        }

        return string.Equals(
                   ReadStringProperty(
                       execution,
                       ReceiptManualOverrideSourcePropertyName),
                   ReceiptManualOverrideSource,
                   StringComparison.Ordinal) &&
               TryReadStoredRecoveryTimestamp(
                   execution,
                   ResolvedAtPropertyName,
                   out var resolvedAt) &&
               TryReadStoredRecoveryTimestamp(
                   execution,
                   ReceiptAbsenceVerifiedAtPropertyName,
                   out var absenceVerifiedAt) &&
               resolvedAt.UtcDateTime.Ticks ==
               absenceVerifiedAt.UtcDateTime.Ticks;
    }

    private static BusinessException ReceiptVerificationRequired() =>
        new(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReceiptVerificationRequired);

    private static BusinessException ResolutionInvalid() =>
        new(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionResolutionInvalid);

    private sealed record ResolutionPreparation(
        PipelineExecution Execution,
        IReadOnlyCollection<string> TargetResources,
        DataSource? ReceiptTarget,
        EtlExecutionContext? ReceiptContext,
        bool ExistingResolution);
}
