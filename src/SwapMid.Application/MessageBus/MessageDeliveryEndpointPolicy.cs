using System;
using System.Linq;
using SwapMid.Infrastructure;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.MessageBus;

/// <summary>消息投递端点安全策略：配置时限制协议与域名白名单，建连时由 OutboundEndpointConnectionGuard 限制实际解析地址。</summary>
public interface IMessageDeliveryEndpointPolicy
{
    Uri Validate(DeliveryType deliveryType, string? endpointUrl);
}

public class MessageDeliveryEndpointPolicy : IMessageDeliveryEndpointPolicy, ITransientDependency
{
    internal const string AllowedHostsKey = ExternalHttpConfigurationKeys.MessageDeliveryAllowedHosts;
    internal const string AllowedPrivateNetworksKey = ExternalHttpConfigurationKeys.MessageDeliveryAllowedPrivateNetworks;
    internal const string BotAllowedHostsKey = ExternalHttpConfigurationKeys.BotDeliveryAllowedHosts;

    private readonly IConfiguration _configuration;

    public MessageDeliveryEndpointPolicy(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Uri Validate(DeliveryType deliveryType, string? endpointUrl)
        => Validate(deliveryType, endpointUrl, _configuration);

    internal static Uri Validate(
        DeliveryType deliveryType,
        string? endpointUrl,
        IConfiguration configuration)
    {
        if (deliveryType == DeliveryType.RabbitMq)
        {
            throw new UserFriendlyException("RabbitMQ 投递不使用 HTTP 端点");
        }

        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.IdnHost) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new UserFriendlyException("消息投递端点必须是无用户信息、无片段的 HTTPS 绝对地址");
        }

        if (deliveryType == DeliveryType.BotWebhook)
        {
            var isAllowedBotHost =
                OutboundEndpointConnectionGuard.DefaultBotWebhookHosts.Any(pattern =>
                    OutboundEndpointConnectionGuard.MatchesHost(uri.IdnHost, pattern)) ||
                OutboundEndpointConnectionGuard.GetConfiguredValues(configuration, BotAllowedHostsKey)
                    .Any(pattern => OutboundEndpointConnectionGuard.MatchesHost(uri.IdnHost, pattern));

            if (!isAllowedBotHost)
            {
                throw new UserFriendlyException(
                    $"机器人 Webhook 域名需为标准平台机器人域名或加入配置白名单 {BotAllowedHostsKey}");
            }

            return uri;
        }

        if (!OutboundEndpointConnectionGuard.GetConfiguredValues(configuration, AllowedHostsKey)
                .Any(pattern => OutboundEndpointConnectionGuard.MatchesHost(uri.IdnHost, pattern)))
        {
            throw new UserFriendlyException(
                $"HTTP 投递域名未加入配置白名单 {AllowedHostsKey}");
        }

        return uri;
    }
}
