using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwapMid.Infrastructure;

public enum BotWebhookProviderKind
{
    Generic = 0,
    Slack = 1,
    Feishu = 2,
    DingTalk = 3,
    WeCom = 4,
}

public sealed record FormattedBotWebhookRequest(
    Uri RequestUri,
    string JsonBody,
    string? SignatureHeaderValue,
    BotWebhookProviderKind Provider);

/// <summary>
/// 通用群机器人与自定义 Webhook 载荷编解码器（支持 Generic JSON、Slack、飞书/Lark、钉钉、企业微信机器人）。
/// </summary>
public static class BotWebhookPayloadFormatter
{
    public static BotWebhookProviderKind DetectProvider(Uri endpoint, string? explicitProvider = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitProvider))
        {
            var normalized = explicitProvider.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "slack":
                    return BotWebhookProviderKind.Slack;
                case "feishu" or "lark":
                    return BotWebhookProviderKind.Feishu;
                case "dingtalk":
                    return BotWebhookProviderKind.DingTalk;
                case "wecom" or "wework":
                    return BotWebhookProviderKind.WeCom;
                case "generic":
                    return BotWebhookProviderKind.Generic;
            }
        }

        var host = endpoint.IdnHost.ToLowerInvariant();
        var path = endpoint.AbsolutePath.ToLowerInvariant();

        if (host.EndsWith("slack.com", StringComparison.Ordinal) || path.StartsWith("/services/", StringComparison.Ordinal))
        {
            return BotWebhookProviderKind.Slack;
        }

        if (host.EndsWith("feishu.cn", StringComparison.Ordinal) ||
            host.EndsWith("larksuite.com", StringComparison.Ordinal) ||
            path.Contains("/open-apis/bot/v2/hook/", StringComparison.Ordinal))
        {
            return BotWebhookProviderKind.Feishu;
        }

        if (host.EndsWith("dingtalk.com", StringComparison.Ordinal) || path.Contains("/robot/send", StringComparison.Ordinal))
        {
            return BotWebhookProviderKind.DingTalk;
        }

        if (host.EndsWith("weixin.qq.com", StringComparison.Ordinal) || path.Contains("/cgi-bin/webhook/send", StringComparison.Ordinal))
        {
            return BotWebhookProviderKind.WeCom;
        }

        return BotWebhookProviderKind.Generic;
    }

    public static FormattedBotWebhookRequest Format(
        Uri endpoint,
        string title,
        string markdownBody,
        string? secret = null,
        string? explicitProvider = null,
        IReadOnlyList<string>? mentions = null,
        IReadOnlyDictionary<string, string?>? metadata = null,
        DateTimeOffset? now = null)
    {
        var provider = DetectProvider(endpoint, explicitProvider);
        var cleanTitle = SanitizeInline(title);
        var cleanBody = SanitizeBlock(markdownBody);
        var mentionList = mentions?
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        var timestamp = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();

        switch (provider)
        {
            case BotWebhookProviderKind.Slack:
            {
                var textBuilder = new StringBuilder();
                textBuilder.Append('*').Append(cleanTitle).Append('*');
                if (!string.IsNullOrWhiteSpace(cleanBody))
                {
                    textBuilder.Append('\n').Append(cleanBody);
                }

                if (mentionList.Length > 0)
                {
                    textBuilder.Append("\nMentions: ").Append(string.Join(", ", mentionList));
                }

                var json = JsonSerializer.Serialize(new { text = textBuilder.ToString() });
                return new FormattedBotWebhookRequest(
                    endpoint,
                    json,
                    string.IsNullOrWhiteSpace(secret) ? null : WebhookSignature.Compute(secret, json),
                    provider);
            }

            case BotWebhookProviderKind.Feishu:
            {
                var textBuilder = new StringBuilder();
                textBuilder.Append(cleanTitle);
                if (!string.IsNullOrWhiteSpace(cleanBody))
                {
                    textBuilder.Append('\n').Append(cleanBody);
                }

                if (mentionList.Length > 0)
                {
                    textBuilder.Append("\n@").Append(string.Join(" @", mentionList));
                }

                string json;
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    var tsString = timestamp.ToString();
                    var sign = ComputeFeishuSign(secret, tsString);
                    json = JsonSerializer.Serialize(new
                    {
                        timestamp = tsString,
                        sign,
                        msg_type = "text",
                        content = new { text = textBuilder.ToString() }
                    });
                }
                else
                {
                    json = JsonSerializer.Serialize(new
                    {
                        msg_type = "text",
                        content = new { text = textBuilder.ToString() }
                    });
                }

                return new FormattedBotWebhookRequest(endpoint, json, null, provider);
            }

            case BotWebhookProviderKind.DingTalk:
            {
                var targetUri = string.IsNullOrWhiteSpace(secret)
                    ? endpoint
                    : AppendDingTalkSign(endpoint, secret, (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds());

                var isAtAll = mentionList.Any(m => string.Equals(m, "@all", StringComparison.OrdinalIgnoreCase) || string.Equals(m, "all", StringComparison.OrdinalIgnoreCase));
                var mobiles = mentionList.Where(m => !string.Equals(m, "@all", StringComparison.OrdinalIgnoreCase) && !string.Equals(m, "all", StringComparison.OrdinalIgnoreCase)).ToArray();

                var md = $"### {cleanTitle}\n{cleanBody}".TrimEnd();
                var json = JsonSerializer.Serialize(new
                {
                    msgtype = "markdown",
                    markdown = new
                    {
                        title = cleanTitle,
                        text = md
                    },
                    at = new
                    {
                        atMobiles = mobiles,
                        isAtAll
                    }
                });

                return new FormattedBotWebhookRequest(targetUri, json, null, provider);
            }

            case BotWebhookProviderKind.WeCom:
            {
                var mdBuilder = new StringBuilder();
                mdBuilder.Append("### ").Append(cleanTitle);
                if (!string.IsNullOrWhiteSpace(cleanBody))
                {
                    mdBuilder.Append('\n').Append(cleanBody);
                }

                if (mentionList.Length > 0)
                {
                    mdBuilder.Append('\n');
                    foreach (var m in mentionList)
                    {
                        var token = m.StartsWith('@') ? m : "@" + m;
                        mdBuilder.Append($"<@{token.TrimStart('@')}> ");
                    }
                }

                var json = JsonSerializer.Serialize(new
                {
                    msgtype = "markdown",
                    markdown = new
                    {
                        content = mdBuilder.ToString().TrimEnd()
                    }
                });

                return new FormattedBotWebhookRequest(endpoint, json, null, provider);
            }

            default:
            {
                var payload = new Dictionary<string, object?>
                {
                    ["title"] = cleanTitle,
                    ["content"] = cleanBody,
                    ["timestamp"] = (now ?? DateTimeOffset.UtcNow).ToString("O"),
                };
                if (mentionList.Length > 0)
                {
                    payload["mentions"] = mentionList;
                }

                if (metadata != null && metadata.Count > 0)
                {
                    payload["metadata"] = metadata;
                }

                var json = JsonSerializer.Serialize(payload);
                return new FormattedBotWebhookRequest(
                    endpoint,
                    json,
                    string.IsNullOrWhiteSpace(secret) ? null : WebhookSignature.Compute(secret, json),
                    provider);
            }
        }
    }

    public static (bool Succeeded, string? ErrorMessage) EvaluateResponseBody(
        BotWebhookProviderKind provider,
        string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return (true, null);
        }

        if (provider is not (BotWebhookProviderKind.Feishu or BotWebhookProviderKind.DingTalk or BotWebhookProviderKind.WeCom))
        {
            return (true, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (true, null);
            }

            if (root.TryGetProperty("errcode", out var errCodeProp) &&
                errCodeProp.ValueKind == JsonValueKind.Number &&
                errCodeProp.TryGetInt32(out var errCode) &&
                errCode != 0)
            {
                var msg = root.TryGetProperty("errmsg", out var errMsg) ? errMsg.GetString() : null;
                return (false, $"errcode={errCode} {msg}".Trim());
            }

            if (root.TryGetProperty("code", out var codeProp) &&
                codeProp.ValueKind == JsonValueKind.Number &&
                codeProp.TryGetInt32(out var code) &&
                code != 0)
            {
                var msg = root.TryGetProperty("msg", out var msgProp) ? msgProp.GetString() : null;
                return (false, $"code={code} {msg}".Trim());
            }

            return (true, null);
        }
        catch (JsonException)
        {
            return (true, null);
        }
    }

    public static string ComputeFeishuSign(string secret, string timestampSeconds)
    {
        var stringToSign = $"{timestampSeconds}\n{secret}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(stringToSign));
        return Convert.ToBase64String(hmac.ComputeHash([]));
    }

    private static Uri AppendDingTalkSign(Uri endpoint, string secret, long timestampMilliseconds)
    {
        var stringToSign = $"{timestampMilliseconds}\n{secret}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sign = Uri.EscapeDataString(Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign))));

        var builder = new UriBuilder(endpoint);
        var prefix = string.IsNullOrWhiteSpace(builder.Query) ? string.Empty : builder.Query.TrimStart('?') + "&";
        builder.Query = $"{prefix}timestamp={timestampMilliseconds}&sign={sign}";
        return builder.Uri;
    }

    private static string SanitizeInline(string text)
        => (text ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('[', '［')
            .Replace(']', '］')
            .Replace('<', '＜')
            .Replace('>', '＞')
            .Trim();

    private static string SanitizeBlock(string text)
        => (text ?? string.Empty)
            .Replace('[', '［')
            .Replace(']', '］')
            .Replace('<', '＜')
            .Replace('>', '＞')
            .Trim();
}
