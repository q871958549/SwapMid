using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// Apache Kafka / Redpanda Debezium 信封解析器与分区位点编解码器：
/// 兼容标准 Debezium JSON 信封（含或不含外层 <c>payload</c>）、SwapMid CloudEvents CDC JSON 与平铺业务 JSON，
/// 并将多分区消费位点编码为确定性单调字符串 <c>kafka:0=12,1=5</c>。
/// </summary>
internal static class KafkaDebeziumEnvelopeParser
{
    private const string CheckpointPrefix = "kafka:";

    internal static Dictionary<int, long> ParseCheckpoints(string? checkpointLsn)
    {
        var map = new Dictionary<int, long>();
        if (string.IsNullOrWhiteSpace(checkpointLsn))
        {
            return map;
        }

        var raw = checkpointLsn.Trim();
        if (raw.StartsWith(CheckpointPrefix, StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[CheckpointPrefix.Length..];
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return map;
        }

        foreach (var pair in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0 || eq >= pair.Length - 1)
            {
                continue;
            }

            if (int.TryParse(pair[..eq], NumberStyles.Integer, CultureInfo.InvariantCulture, out var part) &&
                part >= 0 &&
                long.TryParse(pair[(eq + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) &&
                offset >= 0)
            {
                map[part] = offset;
            }
        }

        return map;
    }

    internal static string NormalizeCheckpointToken(string rawCheckpoint)
    {
        var raw = rawCheckpoint.Trim();
        if (raw.StartsWith(CheckpointPrefix, StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[CheckpointPrefix.Length..];
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "Kafka CDC 位点不能为空，格式示例：kafka:0=0 或 kafka:0=120,1=45。");
        }

        var map = new SortedDictionary<int, long>();
        foreach (var pair in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0 || eq >= pair.Length - 1 ||
                !int.TryParse(pair[..eq], NumberStyles.Integer, CultureInfo.InvariantCulture, out var part) ||
                part is < 0 or > 4096 ||
                !long.TryParse(pair[(eq + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) ||
                offset < 0)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "Kafka CDC 位点格式无效，请使用 kafka:<partition>=<offset>（示例：kafka:0=0,1=10）。");
            }

            map[part] = offset;
        }

        if (map.Count == 0)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "Kafka CDC 位点必须包含至少一个分区偏移量（示例：kafka:0=0）。");
        }

        return FormatCheckpoints(map);
    }

    internal static string FormatCheckpoints(IReadOnlyDictionary<int, long> offsets)
    {
        if (offsets.Count == 0)
        {
            return "kafka:0=0";
        }

        var formatted = CheckpointPrefix + string.Join(
            ",",
            offsets.OrderBy(kv => kv.Key).Select(kv =>
                $"{kv.Key.ToString(CultureInfo.InvariantCulture)}={kv.Value.ToString(CultureInfo.InvariantCulture)}"));
        return formatted.Length <= CdcStreamConsts.MaxCheckpointLsnLength
            ? formatted
            : formatted[..CdcStreamConsts.MaxCheckpointLsnLength];
    }

    internal static bool TryParseMessage(
        string? rawPayload,
        string topicName,
        int partition,
        long offset,
        DateTime fallbackTimestampUtc,
        out PgOutputCapturedChange? change)
    {
        change = null;
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return false; // Kafka tombstone (null/empty value) — skip record while advancing offset
        }

        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = doc.RootElement;
            var body = root;
            if (root.TryGetProperty("payload", out var payloadElem) && payloadElem.ValueKind == JsonValueKind.Object)
            {
                body = payloadElem;
            }

            var schema = "kafka";
            var table = topicName;
            DateTime commitUtc = DateTime.SpecifyKind(fallbackTimestampUtc, DateTimeKind.Utc);

            if (body.TryGetProperty("source", out var sourceElem) && sourceElem.ValueKind == JsonValueKind.Object)
            {
                schema = ReadOptionalString(sourceElem, "schema", "db") ?? schema;
                table = ReadOptionalString(sourceElem, "table", "collection") ?? table;
                var srcTs = ReadOptionalEpochMillis(sourceElem, "ts_ms");
                if (srcTs.HasValue)
                {
                    commitUtc = srcTs.Value;
                }
            }
            else
            {
                schema = ReadOptionalString(body, "schema", "source_schema") ?? schema;
                table = ReadOptionalString(body, "table", "source_table") ?? table;
            }

            var bodyTs = ReadOptionalEpochMillis(body, "ts_ms") ??
                         ReadOptionalIsoTimestamp(body, "commitTimestampUtc", "commit_timestamp_utc", "timestamp");
            if (bodyTs.HasValue)
            {
                commitUtc = bodyTs.Value;
            }

            var rawOp = (ReadOptionalString(body, "op", "operation", "type") ?? "c").Trim().ToLowerInvariant();
            var op = rawOp switch
            {
                "c" or "r" or "create" or "read" or "insert" => "insert",
                "u" or "update" or "upsert" => "update",
                "d" or "delete" or "remove" => "delete",
                _ => "insert",
            };

            var after = ReadOptionalObjectDictionary(body, "after", "data");
            var before = ReadOptionalObjectDictionary(body, "before", "oldData");

            if (after == null && before == null)
            {
                // Raw flat business JSON payload (non-Debezium producer)
                after = ConvertObjectToDictionary(body);
            }

            var lsn = $"kafka:{partition.ToString(CultureInfo.InvariantCulture)}:{offset.ToString(CultureInfo.InvariantCulture)}";
            change = new PgOutputCapturedChange(schema, table, op, lsn, commitUtc, after, before);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadOptionalString(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString()?.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static DateTime? ReadOptionalEpochMillis(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetProperty(key, out var prop) &&
                prop.ValueKind == JsonValueKind.Number &&
                prop.TryGetInt64(out var ms) &&
                ms > 0)
            {
                try
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }
        }

        return null;
    }

    private static DateTime? ReadOptionalIsoTimestamp(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetProperty(key, out var prop) &&
                prop.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(prop.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            {
                return dto.UtcDateTime;
            }
        }

        return null;
    }

    private static Dictionary<string, object?>? ReadOptionalObjectDictionary(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Object)
            {
                return ConvertObjectToDictionary(prop);
            }
        }

        return null;
    }

    private static Dictionary<string, object?> ConvertObjectToDictionary(JsonElement obj)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in obj.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when prop.Value.TryGetInt64(out var l) => l,
                JsonValueKind.Number when prop.Value.TryGetDecimal(out var dec) => dec,
                JsonValueKind.Number => prop.Value.GetDouble(),
                _ => prop.Value.GetRawText(),
            };
        }

        return dict;
    }
}
