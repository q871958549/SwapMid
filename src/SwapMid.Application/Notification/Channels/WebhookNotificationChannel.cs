using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Infrastructure;
using SwapMid.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;

namespace SwapMid.Notification.Channels;

/// <summary>
/// 通用 Webhook / 群机器人通知渠道（支持 Slack、飞书/Lark、钉钉、企业微信机器人与自定义 JSON Webhook）。
/// </summary>
[ExposeServices(typeof(INotificationChannel))]
public class WebhookNotificationChannel : INotificationChannel, ITransientDependency
{
    private const int MaxResponseInspectBytes = 8192;

    private readonly ISettingProvider _settingProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhookNotificationChannel> _logger;

    public WebhookNotificationChannel(
        ISettingProvider settingProvider,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WebhookNotificationChannel> logger)
    {
        _settingProvider = settingProvider;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public NotificationChannelType ChannelType => NotificationChannelType.Webhook;

    public async Task<bool> IsEnabledAsync()
    {
        var enabled = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationWebhookEnabled);
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var url = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationWebhookUrl);
        return TryValidateWebhookUri(url, _configuration, out _);
    }

    public async Task<NotificationSendResult> SendAsync(
        NotificationSendRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rawUrl = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationWebhookUrl);
            if (!TryValidateWebhookUri(rawUrl, _configuration, out var endpoint))
            {
                return NotificationSendResult.Fail("Webhook 地址无效或域名未加入白名单");
            }

            var provider = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationWebhookProvider);
            var secret = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationWebhookSecret);
            var mentionRaw = string.IsNullOrWhiteSpace(request.ToUser)
                ? await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationWebhookMentionUsers)
                : request.ToUser;

            var mentions = string.IsNullOrWhiteSpace(mentionRaw)
                ? []
                : mentionRaw.Split([',', '|', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var levelPrefix = request.Level switch
            {
                NotificationLevel.Critical => "[CRITICAL] ",
                NotificationLevel.Warning => "[WARNING] ",
                _ => "[INFO] "
            };

            var bodyBuilder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(request.Content))
            {
                bodyBuilder.Append(request.Content.Trim());
            }

            if (NotificationActionLink.TryCreate(_configuration["App:FrontendUrl"], request.ActionPath, out var actionUrl))
            {
                var label = string.IsNullOrWhiteSpace(request.ActionLabel)
                    ? NotificationActionLink.DefaultLabel
                    : request.ActionLabel;
                if (bodyBuilder.Length > 0)
                {
                    bodyBuilder.Append('\n');
                }

                bodyBuilder.Append($"{label}: {actionUrl}");
            }

            if (!string.IsNullOrWhiteSpace(request.Source))
            {
                if (bodyBuilder.Length > 0)
                {
                    bodyBuilder.Append('\n');
                }

                bodyBuilder.Append($"来源：{request.Source}");
            }

            var formatted = BotWebhookPayloadFormatter.Format(
                endpoint!,
                title: levelPrefix + request.Title,
                markdownBody: bodyBuilder.ToString(),
                secret: secret,
                explicitProvider: provider,
                mentions: mentions,
                metadata: new Dictionary<string, string?>
                {
                    ["level"] = request.Level.ToString(),
                    ["source"] = request.Source,
                });

            using var content = new StringContent(formatted.JsonBody, Encoding.UTF8, "application/json");
            using var httpMessage = new HttpRequestMessage(HttpMethod.Post, formatted.RequestUri) { Content = content };
            SensitiveOutboundRequestOptions.Mark(httpMessage);
            if (!string.IsNullOrEmpty(formatted.SignatureHeaderValue))
            {
                httpMessage.Headers.TryAddWithoutValidation("X-SwapMid-Signature", formatted.SignatureHeaderValue);
            }

            using var http = _httpClientFactory.CreateClient(ExternalHttpClientNames.NotificationWebhook);
            using var response = await http.SendAsync(
                httpMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return NotificationSendResult.Fail($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[MaxResponseInspectBytes];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            var snippet = total == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, total);
            var (ok, err) = BotWebhookPayloadFormatter.EvaluateResponseBody(formatted.Provider, snippet);
            return ok
                ? NotificationSendResult.Ok()
                : NotificationSendResult.Fail(err ?? "Webhook 返回非成功业务码");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook 通知发送失败 title={Title}", request.Title);
            return NotificationSendResult.Fail(ex.Message);
        }
    }

    internal static bool TryValidateWebhookUri(string? rawUrl, IConfiguration configuration, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(parsed.IdnHost) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        var allowed =
            OutboundEndpointConnectionGuard.DefaultBotWebhookHosts.Any(pattern =>
                OutboundEndpointConnectionGuard.MatchesHost(parsed.IdnHost, pattern)) ||
            OutboundEndpointConnectionGuard.GetConfiguredValues(
                    configuration,
                    ExternalHttpConfigurationKeys.NotificationWebhookAllowedHosts)
                .Any(pattern => OutboundEndpointConnectionGuard.MatchesHost(parsed.IdnHost, pattern));

        if (!allowed)
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
