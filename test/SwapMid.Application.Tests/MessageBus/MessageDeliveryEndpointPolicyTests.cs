using System;
using System.Collections.Generic;
using System.Net;
using SwapMid.Infrastructure;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.MessageBus;

public class MessageDeliveryEndpointPolicyTests
{
    [Fact]
    public void 普通Http投递必须Https且命中显式域名白名单()
    {
        var policy = CreatePolicy(allowedHosts: "hooks.example.com,*.trusted.example");

        policy.Validate(DeliveryType.Http, "https://hooks.example.com/events").Host
            .ShouldBe("hooks.example.com");
        policy.Validate(DeliveryType.Http, "https://team.trusted.example/events").Host
            .ShouldBe("team.trusted.example");

        Should.Throw<UserFriendlyException>(() =>
            policy.Validate(DeliveryType.Http, "http://hooks.example.com/events"));
        Should.Throw<UserFriendlyException>(() =>
            policy.Validate(DeliveryType.Http, "https://trusted.example/events"));
        Should.Throw<UserFriendlyException>(() =>
            policy.Validate(DeliveryType.Http, "https://not-allowed.example/events"));
    }

    [Fact]
    public void 普通Http白名单默认空时应失败关闭()
    {
        var policy = CreatePolicy();

        var exception = Should.Throw<UserFriendlyException>(() =>
            policy.Validate(DeliveryType.Http, "https://hooks.example.com/events"));

        exception.Message.ShouldContain(MessageDeliveryEndpointPolicy.AllowedHostsKey);
    }

    [Theory]
    [InlineData("https://hooks.slack.com/services/T00/B00/XXX", true)]
    [InlineData("https://open.feishu.cn/open-apis/bot/v2/hook/abc", true)]
    [InlineData("https://oapi.dingtalk.com/robot/send?access_token=abc", true)]
    [InlineData("https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=abc", true)]
    [InlineData("http://hooks.slack.com/services/T00/B00/XXX", false)]
    [InlineData("https://evil.example/cgi-bin/webhook/send?key=abc", false)]
    [InlineData("https://hooks.slack.com/services/T00/B00/XXX#frag", false)]
    public void 机器人Webhook必须使用受信任Https主机且无片段(string endpoint, bool accepted)
    {
        var policy = CreatePolicy();

        if (accepted)
        {
            policy.Validate(DeliveryType.BotWebhook, endpoint).Scheme.ShouldBe(Uri.UriSchemeHttps);
        }
        else
        {
            Should.Throw<UserFriendlyException>(() =>
                policy.Validate(DeliveryType.BotWebhook, endpoint));
        }
    }

    [Fact]
    public void 建连地址应永久拒绝Loopback与LinkLocal()
    {
        var configuration = CreateConfiguration(
            allowedHosts: "internal.example",
            allowedPrivateNetworks: "127.0.0.0/8,169.254.0.0/16,::1/128,fe80::/10");

        OutboundEndpointConnectionGuard.IsAddressAllowed(
            IPAddress.Parse("127.0.0.1"), configuration, MessageDeliveryEndpointPolicy.AllowedPrivateNetworksKey).ShouldBeFalse();
        OutboundEndpointConnectionGuard.IsAddressAllowed(
            IPAddress.Parse("169.254.169.254"), configuration, MessageDeliveryEndpointPolicy.AllowedPrivateNetworksKey).ShouldBeFalse();
        OutboundEndpointConnectionGuard.IsAddressAllowed(
            IPAddress.IPv6Loopback, configuration, MessageDeliveryEndpointPolicy.AllowedPrivateNetworksKey).ShouldBeFalse();
        OutboundEndpointConnectionGuard.IsAddressAllowed(
            IPAddress.Parse("fe80::1"), configuration, MessageDeliveryEndpointPolicy.AllowedPrivateNetworksKey).ShouldBeFalse();
    }

    [Fact]
    public void 私网地址仅在显式Cidr内允许且企业微信永不允许私网()
    {
        var denied = CreateConfiguration(allowedHosts: "internal.example");
        var allowed = CreateConfiguration(
            allowedHosts: "internal.example",
            allowedPrivateNetworks: "10.168.20.0/24");
        var address = IPAddress.Parse("10.168.20.15");

        OutboundEndpointConnectionGuard.IsAddressAllowed(
                address, denied, MessageDeliveryEndpointPolicy.AllowedPrivateNetworksKey)
            .ShouldBeFalse();
        OutboundEndpointConnectionGuard.IsAddressAllowed(
                address, allowed, MessageDeliveryEndpointPolicy.AllowedPrivateNetworksKey)
            .ShouldBeTrue();
        OutboundEndpointConnectionGuard.IsAddressAllowed(address, allowed, allowedPrivateNetworksKey: null)
            .ShouldBeFalse();
        OutboundEndpointConnectionGuard.IsAddressAllowed(
            IPAddress.Parse("8.8.8.8"), denied, MessageDeliveryEndpointPolicy.AllowedPrivateNetworksKey).ShouldBeTrue();
    }

    [Fact]
    public void 私网白名单不得用全网段绕过最小授权()
    {
        var configuration = CreateConfiguration(
            allowedHosts: "internal.example",
            allowedPrivateNetworks: "0.0.0.0/0");

        Should.Throw<InvalidOperationException>(() =>
            OutboundEndpointConnectionGuard.IsAddressAllowed(
                IPAddress.Parse("10.168.20.15"),
                configuration,
                MessageDeliveryEndpointPolicy.AllowedPrivateNetworksKey));
    }

    private static MessageDeliveryEndpointPolicy CreatePolicy(
        string? allowedHosts = null,
        string? allowedPrivateNetworks = null)
        => new(CreateConfiguration(allowedHosts, allowedPrivateNetworks));

    private static IConfiguration CreateConfiguration(
        string? allowedHosts = null,
        string? allowedPrivateNetworks = null)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [MessageDeliveryEndpointPolicy.AllowedHostsKey] = allowedHosts,
                [MessageDeliveryEndpointPolicy.AllowedPrivateNetworksKey] = allowedPrivateNetworks,
            })
            .Build();
}
