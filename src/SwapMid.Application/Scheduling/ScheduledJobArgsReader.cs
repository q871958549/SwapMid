using System;
using System.Globalization;
using System.Text.Json;

namespace SwapMid.Scheduling;

/// <summary>
/// 小型定时任务参数读取器：在进入外部 IO 前统一限制 JSON 大小、结构、字段集合与重复字段。
/// 任务仍负责各自的业务范围校验；这里仅提供单个可选字段的严格语法边界。
/// </summary>
internal static class ScheduledJobArgsReader
{
    internal static bool TryReadOptionalString(
        string? argsJson,
        string propertyName,
        out string? value)
    {
        value = null;
        string? parsedValue = null;
        var succeeded = TryReadSingleProperty(argsJson, propertyName, element =>
        {
            if (element.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            parsedValue = element.GetString();
            return !string.IsNullOrWhiteSpace(parsedValue);
        });
        value = parsedValue;
        return succeeded;
    }

    internal static bool TryReadOptionalInt32(
        string? argsJson,
        string propertyName,
        out int? value)
    {
        value = null;
        int? parsedValue = null;
        var succeeded = TryReadSingleProperty(argsJson, propertyName, element =>
        {
            if (element.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out var number))
            {
                parsedValue = number;
                return true;
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = element.GetString();
            if (string.IsNullOrWhiteSpace(text)
                || !int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out number))
            {
                return false;
            }

            parsedValue = number;
            return true;
        });
        value = parsedValue;
        return succeeded;
    }

    private static bool TryReadSingleProperty(
        string? argsJson,
        string propertyName,
        Func<JsonElement, bool> readValue)
    {
        if (argsJson is { Length: > JobRunContext.MaxArgsJsonCharacters })
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(argsJson))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(
                argsJson,
                new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var seen = false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.Ordinal)
                    || seen)
                {
                    return false;
                }

                seen = true;
                if (!readValue(property.Value))
                {
                    return false;
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
