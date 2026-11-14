using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SwapMid.DataIntegration;

/// <summary>数据源配置边界共用的 JSON 严格读取原语。</summary>
internal static class DataSourceConfigJson
{
    internal static bool TryParseObject(string? config, out JsonObject root)
    {
        root = null!;
        if (string.IsNullOrWhiteSpace(config))
        {
            return false;
        }

        try
        {
            if (JsonNode.Parse(config) is not JsonObject parsed)
            {
                return false;
            }

            root = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryFindProperty(
        JsonObject obj,
        string requestedName,
        out string actualName,
        out JsonNode? value)
    {
        var normalizedRequestedName = NormalizeName(requestedName);
        foreach (var property in obj)
        {
            if (NormalizeName(property.Key) == normalizedRequestedName)
            {
                actualName = property.Key;
                value = property.Value;
                return true;
            }
        }

        actualName = string.Empty;
        value = null;
        return false;
    }

    internal static string? ReadOptionalStringStrict(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryFindProperty(obj, name, out _, out var value))
            {
                continue;
            }

            if (value is null)
            {
                return null;
            }

            if (TryGetString(value, out var text))
            {
                return text;
            }

            throw new DataSourceConfigFormatException();
        }

        return null;
    }

    internal static int? ReadOptionalIntStrict(JsonObject obj, string name)
    {
        if (!TryFindProperty(obj, name, out _, out var value))
        {
            return null;
        }

        var number = ReadIntValue(value);
        if (number == 0 && !IsJsonNumericZero(value))
        {
            throw new DataSourceConfigFormatException();
        }

        return number;
    }

    internal static int ReadIntValue(JsonNode? value)
    {
        if (value is not JsonValue jsonValue)
        {
            return 0;
        }

        if (jsonValue.TryGetValue<int>(out var number))
        {
            return number;
        }

        return jsonValue.TryGetValue<string>(out var text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }

    internal static bool? ReadOptionalBoolStrict(JsonObject obj, string name)
    {
        if (!TryFindProperty(obj, name, out _, out var value))
        {
            return null;
        }

        if (!TryGetBoolean(value, out var boolean))
        {
            throw new DataSourceConfigFormatException();
        }

        return boolean;
    }

    internal static bool TryGetBoolean(JsonNode? value, out bool boolean)
    {
        boolean = false;
        if (value is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue<bool>(out boolean))
        {
            return true;
        }

        return jsonValue.TryGetValue<string>(out var text) &&
               bool.TryParse(text, out boolean);
    }

    internal static bool TryGetString(JsonNode? node, out string value)
    {
        value = string.Empty;
        return node is JsonValue jsonValue &&
               jsonValue.TryGetValue<string>(out value!);
    }

    internal static bool HasMeaningfulContent(JsonNode? node)
    {
        if (node is null)
        {
            return false;
        }

        if (TryGetString(node, out var text))
        {
            return !string.IsNullOrWhiteSpace(text);
        }

        return node switch
        {
            JsonObject obj => obj.Any(property => HasMeaningfulContent(property.Value)),
            JsonArray array => array.Any(HasMeaningfulContent),
            _ => true,
        };
    }

    internal static string NormalizeName(string name)
        => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsJsonNumericZero(JsonNode? value)
    {
        if (value is not JsonValue jsonValue)
        {
            return false;
        }

        return jsonValue.TryGetValue<int>(out var number) && number == 0 ||
               jsonValue.TryGetValue<string>(out var text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) &&
               number == 0;
    }
}
