using System;
using System.Text.Json;

namespace SwapMid.MessageBus;

/// <summary>
/// 消息订阅结构化过滤求值器（100% 向下兼容纯文本子串过滤）：
/// <list type="bullet">
///   <item><description><c>order.shipped</c>：不区分大小写匹配 <c>Subject</c> 或 <c>Payload</c> 子串；</description></item>
///   <item><description><c>subject:pipeline.execution.*</c>：按 <c>*</c> / <c>?</c> 通配符匹配 <c>Subject</c>；</description></item>
///   <item><description><c>$.status=Succeeded</c> / <c>$.errorCode!=null</c>：安全解析 JSON 载荷按路径比对标量值；</description></item>
///   <item><description>以 <c>;</c> 连接多条子句表示逻辑与（<c>AND</c>），例如 <c>subject:pipeline.execution.*;$.status=Succeeded</c>。</description></item>
/// </list>
/// </summary>
public static class MessageSubscriptionFilterEvaluator
{
    private const string SubjectPrefix = "subject:";
    private const string JsonPathPrefix = "$.";

    private static readonly JsonDocumentOptions SafeJsonOptions = new()
    {
        MaxDepth = 16,
        AllowTrailingCommas = true,
    };

    public static bool IsMatch(string? filterRule, string subject, string payload)
    {
        if (string.IsNullOrWhiteSpace(filterRule))
        {
            return true;
        }

        subject ??= string.Empty;
        payload ??= string.Empty;

        JsonDocument? parsedDocument = null;
        var jsonParseAttempted = false;

        try
        {
            var remaining = filterRule.AsSpan();
            while (!remaining.IsEmpty)
            {
                var separatorIndex = remaining.IndexOf(';');
                ReadOnlySpan<char> rawClause;
                if (separatorIndex < 0)
                {
                    rawClause = remaining;
                    remaining = ReadOnlySpan<char>.Empty;
                }
                else
                {
                    rawClause = remaining[..separatorIndex];
                    remaining = remaining[(separatorIndex + 1)..];
                }

                var clause = rawClause.Trim();
                if (clause.IsEmpty)
                {
                    continue;
                }

                if (!EvaluateClause(clause, subject, payload, ref parsedDocument, ref jsonParseAttempted))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            parsedDocument?.Dispose();
        }
    }

    private static bool EvaluateClause(
        ReadOnlySpan<char> clause,
        string subject,
        string payload,
        ref JsonDocument? parsedDocument,
        ref bool jsonParseAttempted)
    {
        if (clause.StartsWith(SubjectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var pattern = clause[SubjectPrefix.Length..].Trim();
            return pattern.IsEmpty || WildcardMatch(subject.AsSpan(), pattern);
        }

        if (clause.StartsWith(JsonPathPrefix, StringComparison.Ordinal) &&
            TrySplitJsonComparison(clause, out var jsonPath, out var expectedRaw, out var isNotEqual))
        {
            if (!jsonParseAttempted)
            {
                jsonParseAttempted = true;
                parsedDocument = TryParseJsonObject(payload);
            }

            if (parsedDocument is null)
            {
                return false;
            }

            var expected = Unquote(expectedRaw.Trim());
            var found = TryResolveJsonScalar(parsedDocument.RootElement, jsonPath, out var actualValue);
            if (!found)
            {
                var expectsMissingOrNull = expected.Equals("null", StringComparison.OrdinalIgnoreCase);
                return isNotEqual ? !expectsMissingOrNull : false;
            }

            var equals = string.Equals(
                actualValue,
                expected.ToString(),
                StringComparison.OrdinalIgnoreCase);
            return isNotEqual ? !equals : equals;
        }

        var token = clause.ToString();
        return subject.Contains(token, StringComparison.OrdinalIgnoreCase) ||
               payload.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySplitJsonComparison(
        ReadOnlySpan<char> clause,
        out ReadOnlySpan<char> pathWithoutRoot,
        out ReadOnlySpan<char> expectedValue,
        out bool isNotEqual)
    {
        var body = clause[JsonPathPrefix.Length..];
        var notEqualIndex = body.IndexOf("!=", StringComparison.Ordinal);
        if (notEqualIndex > 0)
        {
            pathWithoutRoot = body[..notEqualIndex].Trim();
            expectedValue = body[(notEqualIndex + 2)..];
            isNotEqual = true;
            return !pathWithoutRoot.IsEmpty;
        }

        var equalIndex = body.IndexOf('=');
        if (equalIndex > 0)
        {
            pathWithoutRoot = body[..equalIndex].Trim();
            expectedValue = body[(equalIndex + 1)..];
            isNotEqual = false;
            return !pathWithoutRoot.IsEmpty;
        }

        pathWithoutRoot = default;
        expectedValue = default;
        isNotEqual = false;
        return false;
    }

    private static ReadOnlySpan<char> Unquote(ReadOnlySpan<char> value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static JsonDocument? TryParseJsonObject(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var doc = JsonDocument.Parse(payload, SafeJsonOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                return null;
            }

            return doc;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryResolveJsonScalar(
        JsonElement root,
        ReadOnlySpan<char> path,
        out string scalar)
    {
        var current = root;
        var remaining = path;

        while (!remaining.IsEmpty)
        {
            var dotIndex = remaining.IndexOf('.');
            ReadOnlySpan<char> segment;
            if (dotIndex < 0)
            {
                segment = remaining;
                remaining = ReadOnlySpan<char>.Empty;
            }
            else
            {
                segment = remaining[..dotIndex];
                remaining = remaining[(dotIndex + 1)..];
            }

            segment = segment.Trim();
            if (segment.IsEmpty || current.ValueKind != JsonValueKind.Object)
            {
                scalar = string.Empty;
                return false;
            }

            if (!TryGetPropertyCaseInsensitive(current, segment, out current))
            {
                scalar = string.Empty;
                return false;
            }
        }

        switch (current.ValueKind)
        {
            case JsonValueKind.String:
                scalar = current.GetString() ?? string.Empty;
                return true;
            case JsonValueKind.Number:
                scalar = current.GetRawText();
                return true;
            case JsonValueKind.True:
                scalar = "true";
                return true;
            case JsonValueKind.False:
                scalar = "false";
                return true;
            case JsonValueKind.Null:
                scalar = "null";
                return true;
            default:
                scalar = string.Empty;
                return false;
        }
    }

    private static bool TryGetPropertyCaseInsensitive(
        JsonElement obj,
        ReadOnlySpan<char> propertyName,
        out JsonElement value)
    {
        if (obj.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in obj.EnumerateObject())
        {
            if (property.Name.AsSpan().Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool WildcardMatch(ReadOnlySpan<char> input, ReadOnlySpan<char> pattern)
    {
        var inputIdx = 0;
        var patternIdx = 0;
        var starIdx = -1;
        var matchIdx = 0;

        while (inputIdx < input.Length)
        {
            if (patternIdx < pattern.Length &&
                (pattern[patternIdx] == '?' ||
                 char.ToUpperInvariant(pattern[patternIdx]) == char.ToUpperInvariant(input[inputIdx])))
            {
                inputIdx++;
                patternIdx++;
            }
            else if (patternIdx < pattern.Length && pattern[patternIdx] == '*')
            {
                starIdx = patternIdx++;
                matchIdx = inputIdx;
            }
            else if (starIdx != -1)
            {
                patternIdx = starIdx + 1;
                inputIdx = ++matchIdx;
            }
            else
            {
                return false;
            }
        }

        while (patternIdx < pattern.Length && pattern[patternIdx] == '*')
        {
            patternIdx++;
        }

        return patternIdx == pattern.Length;
    }
}
