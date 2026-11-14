using System;
using System.Linq;
using System.Text.Json;
using Volo.Abp.Data;

namespace SwapMid.DataIntegration;

/// <summary>DataPipeline 上的 receipt-v1 激活证明；仅存低敏身份与摘要。</summary>
internal static class DataPipelineReceiptActivation
{
    internal const string ProtocolPropertyName =
        "DataPipeline.ReceiptActivation.Protocol";
    internal const string ResourceInstanceIdPropertyName =
        "DataPipeline.ReceiptActivation.ResourceInstanceId";
    internal const string CanonicalTargetTablePropertyName =
        "DataPipeline.ReceiptActivation.CanonicalTargetTable";
    internal const string VerifiedTargetResourceHashPropertyName =
        "DataPipeline.ReceiptActivation.VerifiedTargetResourceHash";
    internal const string ExecutionContractHashPropertyName =
        "DataPipeline.ReceiptActivation.ExecutionContractHash";
    internal const string TargetCoordinationIdentityHashPropertyName =
        "DataPipeline.ReceiptActivation.TargetCoordinationIdentityHash";

    private const string Protocol = "target-receipt-v1";

    internal static void Store(
        DataPipeline pipeline,
        EtlTargetReceiptActivation activation,
        string executionContractHash)
    {
        pipeline.SetProperty(ProtocolPropertyName, Protocol);
        pipeline.SetProperty(
            ResourceInstanceIdPropertyName,
            activation.ResourceInstanceId.ToString("N"));
        pipeline.SetProperty(
            CanonicalTargetTablePropertyName,
            activation.CanonicalTargetTable);
        pipeline.SetProperty(
            VerifiedTargetResourceHashPropertyName,
            activation.VerifiedTargetResourceHash);
        pipeline.SetProperty(
            ExecutionContractHashPropertyName,
            executionContractHash);
        pipeline.SetProperty(
            TargetCoordinationIdentityHashPropertyName,
            activation.TargetCoordinationIdentityHash);
    }

    internal static void Clear(DataPipeline pipeline)
    {
        pipeline.ExtraProperties.Remove(ProtocolPropertyName);
        pipeline.ExtraProperties.Remove(ResourceInstanceIdPropertyName);
        pipeline.ExtraProperties.Remove(CanonicalTargetTablePropertyName);
        pipeline.ExtraProperties.Remove(VerifiedTargetResourceHashPropertyName);
        pipeline.ExtraProperties.Remove(ExecutionContractHashPropertyName);
        pipeline.ExtraProperties.Remove(
            TargetCoordinationIdentityHashPropertyName);
    }

    internal static bool HasAny(DataPipeline pipeline) =>
        pipeline.ExtraProperties.ContainsKey(ProtocolPropertyName) ||
        pipeline.ExtraProperties.ContainsKey(ResourceInstanceIdPropertyName) ||
        pipeline.ExtraProperties.ContainsKey(CanonicalTargetTablePropertyName) ||
        pipeline.ExtraProperties.ContainsKey(
            VerifiedTargetResourceHashPropertyName) ||
        pipeline.ExtraProperties.ContainsKey(ExecutionContractHashPropertyName) ||
        pipeline.ExtraProperties.ContainsKey(
            TargetCoordinationIdentityHashPropertyName);

    internal static Activation? Read(DataPipeline pipeline)
    {
        if (!string.Equals(
                ReadString(pipeline, ProtocolPropertyName),
                Protocol,
                StringComparison.Ordinal) ||
            !Guid.TryParseExact(
                ReadString(pipeline, ResourceInstanceIdPropertyName),
                "N",
                out var instanceId) ||
            instanceId == Guid.Empty)
        {
            return null;
        }

        var canonicalTargetTable = ReadString(
            pipeline,
            CanonicalTargetTablePropertyName);
        var verifiedTargetResourceHash = ReadString(
            pipeline,
            VerifiedTargetResourceHashPropertyName);
        var executionContractHash = ReadString(
            pipeline,
            ExecutionContractHashPropertyName);
        var targetCoordinationIdentityHash = ReadString(
            pipeline,
            TargetCoordinationIdentityHashPropertyName);
        if (!EtlTransformConfig.IsValidTargetTable(canonicalTargetTable) ||
            !IsHash(verifiedTargetResourceHash) ||
            !IsHash(executionContractHash) ||
            !IsHash(targetCoordinationIdentityHash))
        {
            return null;
        }

        return new Activation(
            instanceId,
            canonicalTargetTable!,
            verifiedTargetResourceHash!,
            executionContractHash!,
            targetCoordinationIdentityHash!);
    }

    private static string? ReadString(DataPipeline pipeline, string key)
    {
        if (!pipeline.ExtraProperties.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json =>
                json.GetString(),
            _ => null,
        };
    }

    private static bool IsHash(string? value) =>
        value is { Length: PipelineExecutionConsts.Sha256HexLength } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');

    internal sealed record Activation(
        Guid ResourceInstanceId,
        string CanonicalTargetTable,
        string VerifiedTargetResourceHash,
        string ExecutionContractHash,
        string TargetCoordinationIdentityHash);
}
