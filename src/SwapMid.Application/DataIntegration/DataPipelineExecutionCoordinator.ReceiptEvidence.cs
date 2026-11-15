using System;
using System.Text.Json;
using Volo.Abp.Data;

namespace SwapMid.DataIntegration;

public sealed partial class DataPipelineExecutionCoordinator
{
    private static bool StoreReceiptEvidence(
        PipelineExecution execution,
        EtlTargetReceiptEvidence? evidence)
    {
        if (evidence == null)
        {
            return false;
        }

        var expectedWriteMode = evidence.WriteMode switch
        {
            EtlWriteMode.Append => "append",
            EtlWriteMode.Truncate => "truncate",
            EtlWriteMode.Upsert => "upsert",
            _ => string.Empty,
        };
        if (evidence.ProcessedRows < 0 ||
            !IsSha256Hex(evidence.LegacyTargetResourceHash) ||
            !IsSha256Hex(evidence.VerifiedTargetResourceHash) ||
            !IsSha256Hex(evidence.ExecutionContractHash) ||
            !IsSha256Hex(evidence.SourceQueryHash) ||
            !IsSha256Hex(evidence.TargetSchemaHash) ||
            !IsSha256Hex(evidence.BatchDigest) ||
            execution.EvidenceProtocol != 1 ||
            execution.Id != evidence.RunId ||
            execution.PipelineId != evidence.PipelineId ||
            execution.TargetResourceInstanceId != evidence.ResourceInstanceId ||
            !string.Equals(
                execution.TargetResourceHash,
                evidence.LegacyTargetResourceHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                execution.VerifiedTargetResourceHash,
                evidence.VerifiedTargetResourceHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                execution.ExecutionContractHash,
                evidence.ExecutionContractHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                ReadStringProperty(execution, SourceQuerySha256PropertyName),
                evidence.SourceQueryHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                ReadStringProperty(execution, WriteModePropertyName),
                expectedWriteMode,
                StringComparison.Ordinal))
        {
            throw CoordinationUnavailable();
        }

        var existingSchema = ReadStringProperty(
            execution,
            ReceiptTargetSchemaHashPropertyName);
        var existingDigest = ReadStringProperty(
            execution,
            ReceiptBatchDigestPropertyName);
        var existingRows = ReadLongProperty(
            execution,
            ReceiptPreparedRowsPropertyName);
        var hasAny = execution.ExtraProperties.ContainsKey(
                         ReceiptTargetSchemaHashPropertyName) ||
                     execution.ExtraProperties.ContainsKey(
                         ReceiptBatchDigestPropertyName) ||
                     execution.ExtraProperties.ContainsKey(
                         ReceiptPreparedRowsPropertyName);
        if (hasAny)
        {
            if (!string.Equals(
                    existingSchema,
                    evidence.TargetSchemaHash,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existingDigest,
                    evidence.BatchDigest,
                    StringComparison.Ordinal) ||
                existingRows != evidence.ProcessedRows)
            {
                throw CoordinationUnavailable();
            }

            return false;
        }

        execution.SetProperty(
            ReceiptTargetSchemaHashPropertyName,
            evidence.TargetSchemaHash);
        execution.SetProperty(
            ReceiptBatchDigestPropertyName,
            evidence.BatchDigest);
        execution.SetProperty(
            ReceiptPreparedRowsPropertyName,
            evidence.ProcessedRows);
        return true;
    }

    private static long? ReadLongProperty(
        PipelineExecution execution,
        string propertyName)
    {
        if (!execution.ExtraProperties.TryGetValue(propertyName, out var value))
        {
            return null;
        }

        return value switch
        {
            long number => number,
            int number => number,
            JsonElement { ValueKind: JsonValueKind.Number } json
                when json.TryGetInt64(out var number) => number,
            _ => null,
        };
    }
}
