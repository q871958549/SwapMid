using System;
using System.Linq;
using Volo.Abp;

namespace SwapMid.DataIntegration;

public sealed partial class DataPipelineExecutionCoordinator
{
    private static bool ReceiptMatchesExecution(
        PipelineExecution execution,
        EtlExecutionContext context,
        EtlTargetReceiptEvidence receipt)
    {
        if (receipt.RunId != context.RunId ||
            receipt.PipelineId != context.PipelineId ||
            receipt.ResourceInstanceId !=
                context.TargetReceipt!.ResourceInstanceId ||
            !string.Equals(
                receipt.LegacyTargetResourceHash,
                context.LegacyTargetResourceHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                receipt.VerifiedTargetResourceHash,
                context.VerifiedTargetResourceHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                receipt.ExecutionContractHash,
                context.ExecutionContractHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                receipt.SourceQueryHash,
                context.SourceQueryHash,
                StringComparison.Ordinal) ||
            receipt.WriteMode != context.WriteMode ||
            receipt.ProcessedRows < 0 ||
            !IsSha256Hex(receipt.TargetSchemaHash) ||
            !IsSha256Hex(receipt.BatchDigest))
        {
            return false;
        }

        var hasSchema = execution.ExtraProperties.ContainsKey(
            ReceiptTargetSchemaHashPropertyName);
        var hasDigest = execution.ExtraProperties.ContainsKey(
            ReceiptBatchDigestPropertyName);
        var hasRows = execution.ExtraProperties.ContainsKey(
            ReceiptPreparedRowsPropertyName);
        if (!hasSchema && !hasDigest && !hasRows)
        {
            return true;
        }

        return hasSchema && hasDigest && hasRows &&
               string.Equals(
                   ReadStringProperty(
                       execution,
                       ReceiptTargetSchemaHashPropertyName),
                   receipt.TargetSchemaHash,
                   StringComparison.Ordinal) &&
               string.Equals(
                   ReadStringProperty(
                       execution,
                       ReceiptBatchDigestPropertyName),
                   receipt.BatchDigest,
                   StringComparison.Ordinal) &&
               ReadLongProperty(
                   execution,
                   ReceiptPreparedRowsPropertyName) ==
               receipt.ProcessedRows;
    }

    private static bool TryCreateReceiptContext(
        PipelineExecution execution,
        out EtlExecutionContext context)
    {
        context = null!;
        var sourceQueryHash = ReadStringProperty(
            execution,
            SourceQuerySha256PropertyName);
        var targetTable = ReadStringProperty(
            execution,
            TargetTablePropertyName);
        var writeMode = ReadStringProperty(
            execution,
            WriteModePropertyName) switch
        {
            "append" => EtlWriteMode.Append,
            "truncate" => EtlWriteMode.Truncate,
            _ => (EtlWriteMode?)null,
        };
        if (execution.EvidenceProtocol != 1 ||
            execution.TargetResourceInstanceId is not { } instanceId ||
            instanceId == Guid.Empty ||
            !IsSha256Hex(execution.TargetResourceHash) ||
            !IsSha256Hex(execution.VerifiedTargetResourceHash) ||
            !IsSha256Hex(execution.ExecutionContractHash) ||
            !IsSha256Hex(execution.TargetCoordinationIdentityHash) ||
            !IsSha256Hex(sourceQueryHash) ||
            string.IsNullOrWhiteSpace(targetTable) ||
            !EtlTransformConfig.IsValidTargetTable(targetTable) ||
            !writeMode.HasValue)
        {
            return false;
        }

        context = new EtlExecutionContext(
            execution.Id,
            execution.PipelineId,
            execution.TargetResourceHash!,
            execution.VerifiedTargetResourceHash!,
            execution.ExecutionContractHash!,
            execution.TargetCoordinationIdentityHash!,
            sourceQueryHash!,
            targetTable,
            writeMode.Value,
            new EtlTargetReceiptContext(instanceId));
        return true;
    }

    private static Guid? ReadGuidProperty(
        PipelineExecution execution,
        string propertyName)
    {
        var value = ReadStringProperty(execution, propertyName);
        return Guid.TryParseExact(value, "N", out var parsed) &&
               parsed != Guid.Empty
            ? parsed
            : null;
    }

    private static bool IsSha256Hex(string? value) =>
        value is { Length: PipelineExecutionConsts.Sha256HexLength } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static bool HasAnyReceiptVerificationMarker(
        PipelineExecution execution) =>
        string.Equals(
            ReadStringProperty(execution, ResolutionPropertyName),
            TargetReceiptVerifiedResolution,
            StringComparison.Ordinal) ||
        execution.ExtraProperties.ContainsKey(
            ReceiptVerificationSourcePropertyName) ||
        execution.ExtraProperties.ContainsKey(ReceiptVerifiedAtPropertyName);

    private static bool IsCompleteReceiptVerification(
        PipelineExecution execution)
    {
        if (execution.Status != ExecutionStatus.Succeeded ||
            !string.Equals(
                ReadStringProperty(execution, ResolutionPropertyName),
                TargetReceiptVerifiedResolution,
                StringComparison.Ordinal) ||
            !string.Equals(
                ReadStringProperty(
                    execution,
                    ReceiptVerificationSourcePropertyName),
                ReceiptVerificationSource,
                StringComparison.Ordinal) ||
            !string.Equals(
                ReadStringProperty(execution, VerificationNotePropertyName),
                ReceiptVerificationNote,
                StringComparison.Ordinal) ||
            !TryReadStoredRecoveryTimestamp(
                execution,
                ResolvedAtPropertyName,
                out var resolvedAt) ||
            !TryReadStoredRecoveryTimestamp(
                execution,
                ReceiptVerifiedAtPropertyName,
                out var verifiedAt) ||
            resolvedAt.UtcDateTime.Ticks !=
                verifiedAt.UtcDateTime.Ticks ||
            !Guid.TryParseExact(
                ReadStringProperty(execution, ResolvedByPropertyName),
                "N",
                out var resolvedBy) ||
            resolvedBy == Guid.Empty ||
            !TryCreateReceiptContext(execution, out var context) ||
            !TryReadStoredReceiptEvidence(execution, context, out var evidence))
        {
            return false;
        }

        return ReceiptMatchesExecution(execution, context, evidence);
    }

    internal static bool IsTargetReceiptVerificationEligible(
        PipelineExecution execution) =>
        execution.Status == ExecutionStatus.OutcomeUnknown &&
        !HasAnyReceiptVerificationMarker(execution) &&
        TryCreateReceiptContext(execution, out _);

    private static bool TryReadStoredReceiptEvidence(
        PipelineExecution execution,
        EtlExecutionContext context,
        out EtlTargetReceiptEvidence evidence)
    {
        evidence = null!;
        var schemaHash = ReadStringProperty(
            execution,
            ReceiptTargetSchemaHashPropertyName);
        var batchDigest = ReadStringProperty(
            execution,
            ReceiptBatchDigestPropertyName);
        var rows = ReadLongProperty(
            execution,
            ReceiptPreparedRowsPropertyName);
        if (!IsSha256Hex(schemaHash) || !IsSha256Hex(batchDigest) ||
            !rows.HasValue || rows < 0)
        {
            return false;
        }

        evidence = new EtlTargetReceiptEvidence(
            context.RunId,
            context.PipelineId,
            context.TargetReceipt!.ResourceInstanceId,
            context.LegacyTargetResourceHash,
            context.VerifiedTargetResourceHash!,
            context.ExecutionContractHash!,
            context.SourceQueryHash,
            schemaHash!,
            batchDigest!,
            context.WriteMode,
            rows.Value);
        return true;
    }

    private static BusinessException ReceiptInconclusive() =>
        new(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReceiptInconclusive);

    private static BusinessException ReceiptUnavailable() =>
        new(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineTargetReceiptUnavailable);
}
