using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Volo.Abp;

namespace SwapMid.Controllers.GitOps;

/// <summary>
/// 零第三方依赖的确定性 YAML / JSON 编解码器（支持 SwapMid swapmid.io/v1 声明式集成清单双向无损读写）。
/// </summary>
internal static class IntegrationBundleYamlCodec
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static string SerializeJson(IntegrationBundleDocument bundle)
        => JsonSerializer.Serialize(bundle, JsonOptions);

    internal static string SerializeYaml(IntegrationBundleDocument bundle)
    {
        var json = JsonSerializer.Serialize(bundle, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder(json.Length + 256);
        sb.AppendLine("# SwapMid Declarative Integration Bundle (swapmid.io/v1)");
        sb.AppendLine("# Secret-free portable manifest: passwords, HMAC secrets & API keys are never exported.");
        WriteYamlElement(sb, doc.RootElement, indent: 0, isListItem: false);
        return sb.ToString();
    }

    internal static IntegrationBundleDocument Parse(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            throw new BusinessException("SwapMid:GitOps:EmptyBundle", "声明式集成清单内容不能为空。");
        }

        var trimmed = rawContent.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            return DeserializeValidated(trimmed);
        }

        var lines = PrepareYamlLines(rawContent);
        if (lines.Count == 0)
        {
            throw new BusinessException("SwapMid:GitOps:EmptyBundle", "声明式集成清单内容为空。");
        }

        var index = 0;
        var rootNode = ParseYamlNode(lines, ref index, expectedIndent: lines[0].Indent);
        if (rootNode is not JsonObject)
        {
            throw new BusinessException("SwapMid:GitOps:InvalidBundle", "YAML 清单根节点必须为映射对象（如 schemaVersion: swapmid.io/v1）。");
        }

        return DeserializeValidated(rootNode.ToJsonString());
    }

    private static IntegrationBundleDocument DeserializeValidated(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<IntegrationBundleDocument>(json, JsonOptions);
            if (doc == null)
            {
                throw new BusinessException("SwapMid:GitOps:InvalidBundle", "无法反序列化声明式集成清单。");
            }

            if (!string.Equals(doc.SchemaVersion, "swapmid.io/v1", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException(
                    "SwapMid:GitOps:UnsupportedSchemaVersion",
                    $"不支持的清单版本「{doc.SchemaVersion}」，当前仅支持 swapmid.io/v1。");
            }

            return doc;
        }
        catch (JsonException)
        {
            throw new BusinessException("SwapMid:GitOps:InvalidBundle", "声明式清单 JSON/YAML 语法或字段类型无效。");
        }
    }

    private static void WriteYamlElement(StringBuilder sb, JsonElement el, int indent, bool isListItem)
    {
        var pad = new string(' ', indent);
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var first = true;
                foreach (var prop in el.EnumerateObject())
                {
                    if (!first || !isListItem) sb.Append(pad);
                    first = false;
                    sb.Append(prop.Name).Append(':');
                    if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        if (IsEmptyContainer(prop.Value))
                        {
                            sb.Append(prop.Value.ValueKind == JsonValueKind.Array ? " []" : " {}").AppendLine();
                        }
                        else
                        {
                            sb.AppendLine();
                            WriteYamlElement(sb, prop.Value, indent + 2, isListItem: false);
                        }
                    }
                    else
                    {
                        sb.Append(' ').AppendLine(FormatYamlScalar(prop.Value));
                    }
                }

                if (first) sb.AppendLine("{}");
                break;
            }
            case JsonValueKind.Array:
            {
                var any = false;
                foreach (var item in el.EnumerateArray())
                {
                    any = true;
                    sb.Append(pad).Append("- ");
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        if (IsEmptyContainer(item))
                        {
                            sb.AppendLine(item.ValueKind == JsonValueKind.Array ? "[]" : "{}");
                        }
                        else
                        {
                            WriteYamlElement(sb, item, indent + 2, isListItem: true);
                        }
                    }
                    else
                    {
                        sb.AppendLine(FormatYamlScalar(item));
                    }
                }

                if (!any) sb.Append(pad).AppendLine("[]");
                break;
            }
            default:
                sb.AppendLine(FormatYamlScalar(el));
                break;
        }
    }

    private static bool IsEmptyContainer(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array) return el.GetArrayLength() == 0;
        if (el.ValueKind == JsonValueKind.Object)
        {
            using var en = el.EnumerateObject();
            return !en.MoveNext();
        }

        return false;
    }

    private static string FormatYamlScalar(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => "null",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.String => FormatYamlString(el.GetString() ?? string.Empty),
            _ => el.GetRawText(),
        };
    }

    private static string FormatYamlString(string s)
    {
        if (s.Length == 0) return "\"\"";
        var needsQuotes = s.Contains('\n') || s.Contains('\r') || s.Contains(':') || s.Contains('#') ||
                          s.Contains('"') || s.Contains('\'') || s.Contains('{') || s.Contains('}') ||
                          s.Contains('[') || s.Contains(']') || s.Contains(',') || s.StartsWith(' ') ||
                          s.EndsWith(' ') || s.StartsWith('-') || s.StartsWith('*') || s.StartsWith('&') ||
                          s is "true" or "false" or "null" or "yes" or "no" ||
                          double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
        return needsQuotes ? JsonSerializer.Serialize(s) : s;
    }

    private sealed record YamlLine(int Indent, string Text);

    private static List<YamlLine> PrepareYamlLines(string raw)
    {
        var result = new List<YamlLine>();
        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart(' ');
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed == "---" || trimmed == "...")
            {
                continue;
            }

            result.Add(new YamlLine(line.Length - trimmed.Length, trimmed));
        }

        return result;
    }

    private static JsonNode? ParseYamlNode(List<YamlLine> lines, ref int index, int expectedIndent)
    {
        if (index >= lines.Count) return null;
        return lines[index].Text.StartsWith("- ", StringComparison.Ordinal) || lines[index].Text == "-"
            ? ParseYamlArray(lines, ref index, expectedIndent)
            : ParseYamlObject(lines, ref index, expectedIndent);
    }

    private static JsonArray ParseYamlArray(List<YamlLine> lines, ref int index, int baseIndent)
    {
        var arr = new JsonArray();
        while (index < lines.Count && lines[index].Indent == baseIndent &&
               (lines[index].Text.StartsWith("- ", StringComparison.Ordinal) || lines[index].Text == "-"))
        {
            var rest = lines[index].Text.Length > 2 ? lines[index].Text[2..].TrimStart() : string.Empty;
            index++;
            if (rest.Length == 0)
            {
                if (index < lines.Count && lines[index].Indent > baseIndent)
                {
                    arr.Add(ParseYamlNode(lines, ref index, lines[index].Indent));
                }
                else
                {
                    arr.Add(null);
                }

                continue;
            }

            if (TrySplitKeyValue(rest, out var key, out var valText))
            {
                var itemObj = new JsonObject();
                AssignKeyValue(itemObj, key, valText, lines, ref index, baseIndent + 2);
                while (index < lines.Count && lines[index].Indent >= baseIndent + 2 &&
                       !lines[index].Text.StartsWith("- ", StringComparison.Ordinal))
                {
                    if (TrySplitKeyValue(lines[index].Text, out var nk, out var nv))
                    {
                        var curIndent = lines[index].Indent;
                        index++;
                        AssignKeyValue(itemObj, nk, nv, lines, ref index, curIndent);
                    }
                    else
                    {
                        break;
                    }
                }

                arr.Add(itemObj);
            }
            else
            {
                arr.Add(ParseYamlScalar(rest));
            }
        }

        return arr;
    }

    private static JsonObject ParseYamlObject(List<YamlLine> lines, ref int index, int baseIndent)
    {
        var obj = new JsonObject();
        while (index < lines.Count && lines[index].Indent == baseIndent &&
               !lines[index].Text.StartsWith("- ", StringComparison.Ordinal))
        {
            if (!TrySplitKeyValue(lines[index].Text, out var key, out var valText))
            {
                throw new BusinessException("SwapMid:GitOps:InvalidBundle", $"无法解析 YAML 行：{lines[index].Text}");
            }

            index++;
            AssignKeyValue(obj, key, valText, lines, ref index, baseIndent);
        }

        return obj;
    }

    private static void AssignKeyValue(
        JsonObject target,
        string key,
        string valText,
        List<YamlLine> lines,
        ref int index,
        int keyIndent)
    {
        if (valText.Length > 0)
        {
            target[key] = ParseYamlScalar(valText);
            return;
        }

        if (index < lines.Count && lines[index].Indent > keyIndent)
        {
            target[key] = ParseYamlNode(lines, ref index, lines[index].Indent);
            return;
        }

        target[key] = null;
    }

    private static bool TrySplitKeyValue(string text, out string key, out string valText)
    {
        key = string.Empty;
        valText = string.Empty;
        if (text.StartsWith('"') || text.StartsWith('\'')) return false;
        var colon = text.IndexOf(':');
        if (colon <= 0) return false;
        if (colon + 1 < text.Length && text[colon + 1] != ' ') return false;

        key = text[..colon].Trim();
        valText = colon + 1 < text.Length ? text[(colon + 1)..].Trim() : string.Empty;
        return key.Length > 0;
    }

    private static JsonNode? ParseYamlScalar(string raw)
    {
        if (raw == "null" || raw == "~") return null;
        if (raw == "[]") return new JsonArray();
        if (raw == "{}") return new JsonObject();
        if (raw == "true") return JsonValue.Create(true);
        if (raw == "false") return JsonValue.Create(false);
        if (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length >= 2)
        {
            return JsonValue.Create(JsonSerializer.Deserialize<string>(raw));
        }

        if (raw.StartsWith('\'') && raw.EndsWith('\'') && raw.Length >= 2)
        {
            return JsonValue.Create(raw[1..^1].Replace("''", "'", StringComparison.Ordinal));
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            return JsonValue.Create(l);
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return JsonValue.Create(d);
        }

        return JsonValue.Create(raw);
    }
}
