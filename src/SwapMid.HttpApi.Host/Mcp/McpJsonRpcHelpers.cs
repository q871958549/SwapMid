using System;
using System.Text.Json;
using Volo.Abp;

namespace SwapMid.Mcp;

internal static class McpJsonRpcHelpers
{
    internal static object? ExtractRpcId(JsonElement elem) => elem.ValueKind switch
    {
        JsonValueKind.String => elem.GetString(),
        JsonValueKind.Number when elem.TryGetInt64(out var l) => l,
        JsonValueKind.Number => elem.GetDouble(),
        _ => null,
    };

    internal static string? ReadOptionalString(JsonElement obj, string prop)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString()
            : null;

    internal static Guid ReadRequiredGuid(JsonElement obj, string prop)
        => Guid.TryParse(ReadOptionalString(obj, prop), out var g)
            ? g
            : throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);

    internal static Guid? ReadOptionalGuid(JsonElement obj, string prop)
    {
        var raw = ReadOptionalString(obj, prop);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Guid.TryParse(raw, out var g)
            ? g
            : throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
    }

    internal static int ReadClampedInt(JsonElement obj, string prop, int defaultValue, int min, int max)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var e) && e.TryGetInt32(out var v))
        {
            return Math.Clamp(v, min, max);
        }

        return defaultValue;
    }
}
