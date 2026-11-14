using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Volo.Abp;

namespace SwapMid.DataIntegration;

public sealed partial class DataPipelineExecutionCoordinator
{
    private const int MinRecoveryEvidenceOpaqueIdLength = 8;
    private const int MaxRecoveryEvidenceOpaqueIdLength = 128;
    private static readonly string[] RecoveryEvidencePrefixes =
    [
        "incident:",
        "change:",
        "work-order:",
        "dba-evidence:",
    ];

    private static NormalizedRecovery NormalizeRecoveryInput(
        RecoverPipelineExecutionInput input,
        Guid recovererId,
        DateTime executionStartedAt,
        DateTimeOffset now)
    {
        var expectedStamp = input.ExpectedConcurrencyStamp;
        if (recovererId == Guid.Empty ||
            !IsSafeExpectedConcurrencyStamp(expectedStamp))
        {
            throw RecoveryInvalid();
        }

        var evidence = NormalizeEvidenceReference(
            input.EvidenceReference);
        if (evidence == null)
        {
            throw RecoveryInvalid();
        }

        var startedAt = ToUtc(executionStartedAt);
        var drainStartedAt = input.DrainStartedAt.ToUniversalTime();
        var ownerExitAt = input.OldOwnerExitVerifiedAt.ToUniversalTime();
        var targetExitAt = input.TargetSessionExitVerifiedAt.ToUniversalTime();
        var recoveredAt = now.ToUniversalTime();
        if (startedAt > drainStartedAt ||
            drainStartedAt > ownerExitAt ||
            ownerExitAt > targetExitAt ||
            targetExitAt > recoveredAt ||
            recoveredAt - drainStartedAt < MinimumRecoveryDrain)
        {
            throw RecoveryInvalid();
        }

        return new NormalizedRecovery(
            input.RecoveryRequestId,
            expectedStamp,
            drainStartedAt,
            ownerExitAt,
            targetExitAt,
            evidence,
            recovererId,
            RecoveryProtocolVersion);
    }

    private static string? NormalizeEvidenceReference(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        string? matchedPrefix = null;
        foreach (var prefix in RecoveryEvidencePrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                matchedPrefix = prefix;
                break;
            }
        }

        if (matchedPrefix == null)
        {
            return null;
        }

        var opaqueId = value.AsSpan(matchedPrefix.Length);
        if (opaqueId.Length is < MinRecoveryEvidenceOpaqueIdLength or
            > MaxRecoveryEvidenceOpaqueIdLength ||
            !char.IsAsciiLetterOrDigit(opaqueId[0]) ||
            !char.IsAsciiLetterOrDigit(opaqueId[^1]))
        {
            return null;
        }

        foreach (var character in opaqueId)
        {
            if (!char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_')
            {
                return null;
            }
        }

        return value;
    }

    private static bool IsSafeExpectedConcurrencyStamp(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= 40 &&
           string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
           value.All(character =>
               char.IsAsciiLetterOrDigit(character) ||
               character is '-' or '_');

    private static DateTimeOffset ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value),
            DateTimeKind.Local => new DateTimeOffset(value).ToUniversalTime(),
            _ => new DateTimeOffset(
                DateTime.SpecifyKind(value, DateTimeKind.Utc)),
        };

    private static bool HasSameRecoveryRequestId(
        PipelineExecution execution,
        Guid recoveryRequestId)
        => string.Equals(
            ReadStringProperty(execution, RecoveryRequestIdPropertyName),
            recoveryRequestId.ToString("N"),
            StringComparison.Ordinal);

    private static bool IsExistingRecovery(
        PipelineExecution execution,
        NormalizedRecovery recovery)
    {
        if (!HasSameRecoveryRequestId(execution, recovery.RequestId))
        {
            return false;
        }

        // marker 与状态在同一事务提交。相同 requestId 却仍为 Running 或缺审计键只可能
        // 是库损坏/绕过协议的写入，不能伪装成恢复成功回放。
        if (!HasCompleteRunningRecoveryMarker(execution))
        {
            throw CoordinationUnavailable();
        }

        if (!DoesRecoveryPayloadMatch(execution, recovery))
        {
            throw RecoveryConflict();
        }

        return true;
    }

    private static bool DoesRecoveryPayloadMatch(
        PipelineExecution execution,
        NormalizedRecovery recovery)
        => string.Equals(
               ReadStringProperty(execution, RecoveredByPropertyName),
               recovery.RecovererId.ToString("N"),
               StringComparison.Ordinal) &&
           string.Equals(
               ReadStringProperty(
                   execution,
                   RecoveryExpectedConcurrencyStampPropertyName),
               recovery.ExpectedConcurrencyStamp,
               StringComparison.Ordinal) &&
           IsStoredRecoveryTimestampEqual(
               execution,
               RecoveryDrainStartedAtPropertyName,
               recovery.DrainStartedAt) &&
           IsStoredRecoveryTimestampEqual(
               execution,
               RecoveryOldOwnerExitVerifiedAtPropertyName,
               recovery.OldOwnerExitVerifiedAt) &&
           IsStoredRecoveryTimestampEqual(
               execution,
               RecoveryTargetSessionExitVerifiedAtPropertyName,
               recovery.TargetSessionExitVerifiedAt) &&
           string.Equals(
               ReadStringProperty(
                   execution,
                   RecoveryEvidenceReferencePropertyName),
               recovery.EvidenceReference,
               StringComparison.Ordinal) &&
           string.Equals(
               ReadStringProperty(
                   execution,
                   RecoveryProtocolVersionPropertyName),
               recovery.ProtocolVersion,
               StringComparison.Ordinal);

    private static bool IsStoredRecoveryTimestampEqual(
        PipelineExecution execution,
        string propertyName,
        DateTimeOffset expected)
        => TryReadStoredRecoveryTimestamp(
               execution,
               propertyName,
               out var stored) &&
           stored.UtcDateTime.Ticks ==
           expected.ToUniversalTime().UtcDateTime.Ticks;

    private static bool TryReadStoredRecoveryTimestamp(
        PipelineExecution execution,
        string propertyName,
        out DateTimeOffset value)
    {
        value = default;
        if (!execution.ExtraProperties.TryGetValue(
                propertyName,
                out var stored))
        {
            return false;
        }

        switch (stored)
        {
            case DateTimeOffset offset:
                value = offset.ToUniversalTime();
                return true;
            case DateTime dateTime:
                value = ToUtc(dateTime);
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } json:
                stored = json.GetString();
                break;
        }

        return stored is string text &&
               DateTimeOffset.TryParse(
                   text,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out value);
    }

    private static bool HasRunningRecoveryMarker(
        PipelineExecution execution)
        => execution.ExtraProperties.ContainsKey(
               RecoveryRequestIdPropertyName) ||
           execution.ExtraProperties.ContainsKey(
               RecoveredAtPropertyName) ||
           execution.ExtraProperties.ContainsKey(
               RecoveredByPropertyName) ||
           execution.ExtraProperties.ContainsKey(
               RecoveryExpectedConcurrencyStampPropertyName) ||
           execution.ExtraProperties.ContainsKey(
               RecoveryDrainStartedAtPropertyName) ||
           execution.ExtraProperties.ContainsKey(
               RecoveryOldOwnerExitVerifiedAtPropertyName) ||
           execution.ExtraProperties.ContainsKey(
               RecoveryTargetSessionExitVerifiedAtPropertyName) ||
           execution.ExtraProperties.ContainsKey(
               RecoveryEvidenceReferencePropertyName) ||
           execution.ExtraProperties.ContainsKey(
               RecoveryProtocolVersionPropertyName);

    private static bool HasCompleteRunningRecoveryMarker(
        PipelineExecution execution)
    {
        if (execution.Status is not (
                ExecutionStatus.OutcomeUnknown or
                ExecutionStatus.Succeeded or
                ExecutionStatus.Failed) ||
            !TryReadCanonicalGuid(
                execution,
                RecoveryRequestIdPropertyName,
                out _) ||
            !TryReadCanonicalGuid(
                execution,
                RecoveredByPropertyName,
                out _) ||
            !IsSafeExpectedConcurrencyStamp(
                ReadStringProperty(
                    execution,
                    RecoveryExpectedConcurrencyStampPropertyName)) ||
            !TryReadStoredRecoveryTimestamp(
                execution,
                RecoveredAtPropertyName,
                out var recoveredAt) ||
            !TryReadStoredRecoveryTimestamp(
                execution,
                RecoveryDrainStartedAtPropertyName,
                out var drainStartedAt) ||
            !TryReadStoredRecoveryTimestamp(
                execution,
                RecoveryOldOwnerExitVerifiedAtPropertyName,
                out var ownerExitAt) ||
            !TryReadStoredRecoveryTimestamp(
                execution,
                RecoveryTargetSessionExitVerifiedAtPropertyName,
                out var targetExitAt))
        {
            return false;
        }

        var evidence = ReadStringProperty(
            execution,
            RecoveryEvidenceReferencePropertyName);
        return evidence != null &&
               string.Equals(
                   NormalizeEvidenceReference(evidence),
                   evidence,
                   StringComparison.Ordinal) &&
               string.Equals(
                   ReadStringProperty(
                       execution,
                       RecoveryProtocolVersionPropertyName),
                   RecoveryProtocolVersion,
                   StringComparison.Ordinal) &&
               ToUtc(execution.StartedAt) <=
               drainStartedAt.ToUniversalTime() &&
               drainStartedAt <= ownerExitAt &&
               ownerExitAt <= targetExitAt &&
               targetExitAt <= recoveredAt &&
               recoveredAt - drainStartedAt >= MinimumRecoveryDrain &&
               HasCompleteRecoveryResolutionAudit(
                   execution,
                   recoveredAt);
    }

    private static bool HasCompleteRecoveryResolutionAudit(
        PipelineExecution execution,
        DateTimeOffset recoveredAt)
    {
        if (execution.Status == ExecutionStatus.OutcomeUnknown)
        {
            return !execution.ExtraProperties.ContainsKey(
                       ResolutionPropertyName) &&
                   !execution.ExtraProperties.ContainsKey(
                       ResolvedAtPropertyName) &&
                   !execution.ExtraProperties.ContainsKey(
                       ResolvedByPropertyName) &&
                   !execution.ExtraProperties.ContainsKey(
                       VerificationNotePropertyName);
        }

        if (execution.Status == ExecutionStatus.Succeeded &&
            HasAnyReceiptVerificationMarker(execution))
        {
            return IsCompleteReceiptVerification(execution) &&
                   TryReadStoredRecoveryTimestamp(
                       execution,
                       ResolvedAtPropertyName,
                       out var receiptResolvedAt) &&
                   receiptResolvedAt >= recoveredAt;
        }

        var expectedResolution = execution.Status switch
        {
            ExecutionStatus.Succeeded =>
                PipelineExecutionResolution.ConfirmedApplied.ToString(),
            ExecutionStatus.Failed =>
                PipelineExecutionResolution.ConfirmedNotApplied.ToString(),
            _ => null,
        };
        if (expectedResolution == null ||
            !string.Equals(
                ReadStringProperty(execution, ResolutionPropertyName),
                expectedResolution,
                StringComparison.Ordinal) ||
            !TryReadStoredRecoveryTimestamp(
                execution,
                ResolvedAtPropertyName,
                out var resolvedAt) ||
            resolvedAt < recoveredAt ||
            !TryReadCanonicalGuid(
                execution,
                ResolvedByPropertyName,
                out _))
        {
            return false;
        }

        var note = ReadStringProperty(
            execution,
            VerificationNotePropertyName);
        return note != null &&
               string.Equals(
                   note,
                   NormalizeEvidenceReference(note),
                   StringComparison.Ordinal) &&
               (execution.Status != ExecutionStatus.Failed ||
                HasCompleteReceiptManualNotAppliedAudit(execution));
    }

    private static bool TryReadCanonicalGuid(
        PipelineExecution execution,
        string propertyName,
        out Guid value)
    {
        var text = ReadStringProperty(execution, propertyName);
        return Guid.TryParseExact(text, "N", out value) &&
               value != Guid.Empty;
    }

    private static BusinessException RecoveryInvalid()
        => new(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionRecoveryInvalid);

    private static BusinessException RecoveryConflict()
        => new(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionRecoveryConflict);

    private sealed record RecoveryPreparation(
        PipelineExecution Execution,
        Guid PipelineId,
        System.Collections.Generic.IReadOnlyCollection<string> TargetResources,
        NormalizedRecovery Recovery,
        bool ExistingRecovery);

    private sealed record RecoveryResult(
        PipelineExecution Execution,
        bool ExistingRecovery);

    private sealed record NormalizedRecovery(
        Guid RequestId,
        string ExpectedConcurrencyStamp,
        DateTimeOffset DrainStartedAt,
        DateTimeOffset OldOwnerExitVerifiedAt,
        DateTimeOffset TargetSessionExitVerifiedAt,
        string EvidenceReference,
        Guid RecovererId,
        string ProtocolVersion);
}
