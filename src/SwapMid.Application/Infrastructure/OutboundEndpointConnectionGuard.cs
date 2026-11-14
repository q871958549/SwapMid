using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace SwapMid.Infrastructure;

/// <summary>
/// 动态外呼的通用建连护栏（DNS 解析后二次核验目标 IP 网段，防御 SSRF 与 DNS Rebinding）。
/// </summary>
internal static class OutboundEndpointConnectionGuard
{
    internal static readonly string[] DefaultBotWebhookHosts =
    [
        "hooks.slack.com",
        "open.feishu.cn",
        "open.larksuite.com",
        "oapi.dingtalk.com",
        "qyapi.weixin.qq.com",
    ];

    private static readonly IPNetwork[] PrivateNetworks =
    [
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("fc00::/7"),
    ];

    private static readonly IPNetwork[] AlwaysDeniedNetworks =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
        IPNetwork.Parse("::/128"),
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("ff00::/8"),
    ];

    internal static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endpoint,
        IConfiguration configuration,
        string? allowedHostsKey,
        string? allowedPrivateNetworksKey,
        string? fixedHost,
        int? fixedPort,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? builtInAllowedHosts = null)
    {
        ValidateConnectionHost(endpoint, configuration, allowedHostsKey, fixedHost, fixedPort, builtInAllowedHosts);

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            throw new HttpRequestException("外呼目标域名无法解析", ex);
        }

        var allowedAddresses = addresses
            .Select(NormalizeAddress)
            .Where(address => IsAddressAllowed(address, configuration, allowedPrivateNetworksKey))
            .Distinct()
            .ToArray();
        if (allowedAddresses.Length == 0)
        {
            throw new HttpRequestException("外呼目标解析到未授权网络");
        }

        Exception? lastError = null;
        foreach (var address in allowedAddresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                lastError = ex;
                if (ex is OperationCanceledException)
                {
                    throw;
                }
            }
        }

        throw new HttpRequestException("外呼目标连接失败", lastError);
    }

    internal static bool IsAddressAllowed(
        IPAddress address,
        IConfiguration configuration,
        string? allowedPrivateNetworksKey)
    {
        address = NormalizeAddress(address);
        if (IPAddress.IsLoopback(address) || Contains(AlwaysDeniedNetworks, address))
        {
            return false;
        }

        var isPrivate = Contains(PrivateNetworks, address) ||
                        (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv6SiteLocal);
        if (!isPrivate)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(allowedPrivateNetworksKey))
        {
            return false;
        }

        return GetConfiguredValues(configuration, allowedPrivateNetworksKey)
            .Select(value => ParsePrivateNetwork(value, allowedPrivateNetworksKey))
            .Any(network => network.BaseAddress.AddressFamily == address.AddressFamily && network.Contains(address));
    }

    internal static bool MatchesHost(string host, string configuredPattern)
    {
        host = host.Trim().TrimEnd('.');
        var pattern = configuredPattern.Trim().TrimEnd('.');
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[1..];
            return host.Length > suffix.Length && host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyCollection<string> GetConfiguredValues(
        IConfiguration configuration,
        string key)
    {
        var section = configuration.GetSection(key);
        return section.GetChildren()
            .Select(child => child.Value)
            .Append(section.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ValidateConnectionHost(
        DnsEndPoint endpoint,
        IConfiguration configuration,
        string? allowedHostsKey,
        string? fixedHost,
        int? fixedPort,
        IReadOnlyCollection<string>? builtInAllowedHosts = null)
    {
        if (!string.IsNullOrWhiteSpace(fixedHost))
        {
            if ((fixedPort.HasValue && endpoint.Port != fixedPort.Value) ||
                !string.Equals(endpoint.Host.TrimEnd('.'), fixedHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpRequestException("外呼连接目标不在固定白名单内");
            }

            return;
        }

        if (builtInAllowedHosts != null &&
            builtInAllowedHosts.Any(pattern => MatchesHost(endpoint.Host, pattern)))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(allowedHostsKey) ||
            !GetConfiguredValues(configuration, allowedHostsKey)
                .Any(pattern => MatchesHost(endpoint.Host, pattern)))
        {
            throw new HttpRequestException("外呼连接目标不在配置白名单内");
        }
    }

    private static IPNetwork ParsePrivateNetwork(string value, string configurationKey)
    {
        IPNetwork network;
        try
        {
            network = IPNetwork.Parse(value);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new InvalidOperationException($"{configurationKey} 包含非法 CIDR", ex);
        }

        var isPrivateSubnet = PrivateNetworks.Any(privateNetwork =>
            privateNetwork.BaseAddress.AddressFamily == network.BaseAddress.AddressFamily &&
            network.PrefixLength >= privateNetwork.PrefixLength &&
            privateNetwork.Contains(network.BaseAddress));
        if (!isPrivateSubnet)
        {
            throw new InvalidOperationException(
                $"{configurationKey} 仅允许完全落在 RFC1918、CGNAT 或 IPv6 ULA 内的 CIDR");
        }

        return network;
    }

    private static bool Contains(IEnumerable<IPNetwork> networks, IPAddress address)
        => networks.Any(network =>
            network.BaseAddress.AddressFamily == address.AddressFamily && network.Contains(address));

    private static IPAddress NormalizeAddress(IPAddress address)
        => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
