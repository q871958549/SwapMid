using System;
using System.Net.Http;
using SwapMid.Infrastructure;

namespace SwapMid;

/// <summary>
/// 在 Activity 创建前排除 URL 路径或查询串中携带 Webhook 密钥的外呼（防密钥进入 OTel Trace）。
/// </summary>
public static class SensitiveOutboundTelemetryFilter
{
    public static bool ShouldTrace(HttpRequestMessage request)
    {
        if (SensitiveOutboundRequestOptions.IsMarked(request))
        {
            return false;
        }

        var uri = request.RequestUri;
        if (uri == null)
        {
            return true;
        }

        // Slack / Feishu / DingTalk / WeCom Bot Webhook 往往把凭据直接放在路径或查询参数中
        if (string.Equals(uri.Host, "hooks.slack.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith("/services/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if ((string.Equals(uri.Host, "open.feishu.cn", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Host, "open.larksuite.com", StringComparison.OrdinalIgnoreCase)) &&
            uri.AbsolutePath.StartsWith("/open-apis/bot/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(uri.Host, "oapi.dingtalk.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith("/robot/send", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(uri.Host, "qyapi.weixin.qq.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith("/cgi-bin/webhook/send", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
