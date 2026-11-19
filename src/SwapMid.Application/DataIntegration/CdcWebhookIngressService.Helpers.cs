using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace SwapMid.DataIntegration;

public sealed partial class CdcWebhookIngressService
{
    [GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,96}$", RegexOptions.CultureInvariant)]
    private static partial Regex DeliveryIdRegex();

    internal static void EnsurePushWebhookSource(DataSource source)
    {
        if (source.Type != DataSourceType.RestApi)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"Push Webhook CDC 源必须绑定 RestApi 类型数据源（用于安全托管加密 webhookSecret），当前为 {source.Type}。");
        }

        if (!TryExtractWebhookSecret(source.Config, out _))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"Push Webhook 绑定的 RestApi 数据源 JSON 必须包含长度至少 {MinWebhookSecretLength} 字符的 webhookSecret（或 hmacSecret）。");
        }
    }

    internal static bool TryExtractWebhookSecret(string? rawConfig, out string secret)
    {
        secret = string.Empty;
        if (!DataSourceConfigJson.TryParseObject(rawConfig, out var root))
        {
            return false;
        }

        var candidate = DataSourceConfigJson.ReadOptionalStringStrict(
            root, "webhookSecret", "hmacSecret", "signingSecret", "secret")?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length < MinWebhookSecretLength)
        {
            return false;
        }

        secret = candidate;
        return true;
    }

    internal static string ComputeSignature(string secret, ReadOnlySpan<byte> rawBodyBytes)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(rawBodyBytes.ToArray());
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static bool VerifySignature(string secret, ReadOnlySpan<byte> rawBodyBytes, string? providedHeader)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(providedHeader))
        {
            return false;
        }

        var trimmed = providedHeader.Trim();
        if (!trimmed.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hexPart = trimmed["sha256=".Length..];
        if (hexPart.Length != 64)
        {
            return false;
        }

        byte[] providedHash;
        try
        {
            providedHash = Convert.FromHexString(hexPart);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedHash = hmac.ComputeHash(rawBodyBytes.ToArray());
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }

    internal static string NormalizeWebhookCheckpoint(string rawCheckpoint)
    {
        var trimmed = rawCheckpoint.Trim();
        var digits = trimmed.StartsWith("webhook:", StringComparison.OrdinalIgnoreCase)
            ? trimmed["webhook:".Length..]
            : trimmed;
        if (!long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq) || seq < 0)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "Push Webhook 位点必须为非负序号（如 webhook:0 或 0）。");
        }

        return $"webhook:{seq.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static long ParseWebhookSequence(string? checkpointLsn)
    {
        if (string.IsNullOrWhiteSpace(checkpointLsn))
        {
            return 0L;
        }

        var trimmed = checkpointLsn.Trim();
        var digits = trimmed.StartsWith("webhook:", StringComparison.OrdinalIgnoreCase)
            ? trimmed["webhook:".Length..]
            : trimmed;
        return long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq) && seq > 0
            ? seq
            : 0L;
    }

    internal static CdcPreflightResultDto BuildPreflight(DataSource source, CdcStreamDefinition stream)
    {
        if (source.Type != DataSourceType.RestApi)
        {
            return new CdcPreflightResultDto
            {
                IsReady = false,
                WalLevel = "push_webhook",
                PostgreSqlVersion = "HMAC-SHA256",
                Diagnostics = [$"Push Webhook 源必须绑定 RestApi 类型数据源，当前为 {source.Type}。"],
                Summary = "Push Webhook 数据源类型不匹配。",
            };
        }

        if (!TryExtractWebhookSecret(source.Config, out _))
        {
            return new CdcPreflightResultDto
            {
                IsReady = false,
                WalLevel = "push_webhook",
                PostgreSqlVersion = "HMAC-SHA256",
                Diagnostics = [$"关联的 RestApi 数据源缺少有效的 webhookSecret（长度至少 {MinWebhookSecretLength} 字符）。"],
                Summary = "Push Webhook 签名密钥未配置。",
            };
        }

        var endpoint = $"/api/v1/cdc-webhooks/{stream.Id:D}";
        return new CdcPreflightResultDto
        {
            IsReady = true,
            WalLevel = "push_webhook",
            PostgreSqlVersion = "HMAC-SHA256",
            PublicationExists = true,
            SlotExists = true,
            SlotActive = stream.Status == CdcStreamStatus.Active,
            SlotPlugin = "hmac-sha256",
            ConfirmedFlushLsn = stream.CheckpointLsn,
            CurrentWalLsn = stream.CheckpointLsn,
            Diagnostics =
            [
                $"推送接入端点：POST {endpoint}（请求头 X-SwapMid-Signature: sha256=<HMAC-SHA256 hex>）。",
                "Push Webhook 为无服务器事件推送模式，激活后无需轮询数据库连接。",
            ],
            Summary = $"Push Webhook 签名凭据已就绪（接入端点 POST {endpoint}）。",
        };
    }

    private static string ResolveDeliveryId(string? headerValue, byte[] rawBodyBytes)
    {
        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            var trimmed = headerValue.Trim();
            if (DeliveryIdRegex().IsMatch(trimmed))
            {
                return trimmed;
            }
        }

        return "sha256-" + Convert.ToHexString(SHA256.HashData(rawBodyBytes)).ToLowerInvariant()[..24];
    }

    private static bool TryParsePayloadEvents(
        byte[] rawBodyBytes,
        CdcStreamDefinition stream,
        long baseSeq,
        DateTime nowUtc,
        out List<PgOutputCapturedChange> changes,
        out string error)
    {
        changes = new List<PgOutputCapturedChange>();
        error = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(rawBodyBytes);
            var root = doc.RootElement;
            var rawItems = new List<JsonElement>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray()) rawItems.Add(item);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("events", out var eventsProp) && eventsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in eventsProp.EnumerateArray()) rawItems.Add(item);
                }
                else
                {
                    rawItems.Add(root);
                }
            }
            else
            {
                error = "Webhook JSON 载荷必须为对象、事件数组或包含 events 数组的对象。";
                return false;
            }

            if (rawItems.Count == 0)
            {
                error = "Webhook 事件批次不能为空。";
                return false;
            }

            if (rawItems.Count > stream.MaxBatchSize)
            {
                error = $"单次 Webhook 推送事件数（{rawItems.Count}）超过流单批上限（{stream.MaxBatchSize}）。";
                return false;
            }

            for (var i = 0; i < rawItems.Count; i++)
            {
                var item = rawItems[i];
                if (item.ValueKind != JsonValueKind.Object)
                {
                    error = $"事件数组中第 {i} 项不是有效的 JSON 对象。";
                    return false;
                }

                var schema = ReadOptionalString(item, "schema", "source_schema") ?? "webhook";
                var table = ReadOptionalString(item, "table", "source_table") ?? stream.Name;
                var rawOp = (ReadOptionalString(item, "op", "operation", "type") ?? "insert").Trim().ToLowerInvariant();
                var op = rawOp switch
                {
                    "c" or "create" or "insert" => "insert",
                    "u" or "update" or "upsert" => "update",
                    "d" or "delete" or "remove" => "delete",
                    _ => "insert",
                };
                var tsUtc = ReadOptionalTimestamp(item, "commitTimestampUtc", "timestamp", "commit_timestamp_utc") ?? nowUtc;
                var after = ReadOptionalObjectDictionary(item, "data", "after", "payload");
                var before = ReadOptionalObjectDictionary(item, "oldData", "before");
                if (after == null && before == null)
                {
                    after = ConvertObjectToDictionary(item);
                }

                var lsn = $"webhook:{(baseSeq + i + 1).ToString(CultureInfo.InvariantCulture)}";
                changes.Add(new PgOutputCapturedChange(schema, table, op, lsn, tsUtc, after, before));
            }

            return true;
        }
        catch (JsonException)
        {
            error = "Webhook 请求体不是有效的 UTF-8 JSON 格式。";
            return false;
        }
    }

    private static string? ReadOptionalString(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var s = prop.GetString()?.Trim();
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }

        return null;
    }

    private static DateTime? ReadOptionalTimestamp(JsonElement obj, params string[] keys)
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
