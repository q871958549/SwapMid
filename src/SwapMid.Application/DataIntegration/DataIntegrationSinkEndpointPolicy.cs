using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SwapMid.Infrastructure;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// DataIntegration 出站目标（ClickHouse / ObjectStorage / Kafka）统一 SSRF 与私网准入校验策略。
/// </summary>
internal static class DataIntegrationSinkEndpointPolicy
{
    internal static async Task ValidateHttpEndpointAsync(
        Uri endpointUri,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        EnsureAllowedHost(endpointUri.Host, configuration);
        await EnsureAllowedNetworkAddressesAsync(endpointUri.Host, configuration, cancellationToken);
    }

    internal static async Task ValidateKafkaBrokersAsync(
        IReadOnlyList<(string Host, int Port)> brokers,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        foreach (var (host, _) in brokers)
        {
            EnsureAllowedHost(host, configuration);
            await EnsureAllowedNetworkAddressesAsync(host, configuration, cancellationToken);
        }
    }

    internal static void EnsureAllowedHost(string host, IConfiguration configuration)
    {
        var allowedHosts = OutboundEndpointConnectionGuard.GetConfiguredValues(
            configuration,
            ExternalHttpConfigurationKeys.DataIntegrationSinkAllowedHosts);

        if (!allowedHosts.Any(pattern => OutboundEndpointConnectionGuard.MatchesHost(host, pattern)))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.DataSourceConfigInvalid)
                .WithData(
                    "Reason",
                    $"目标主机 {host} 未加入安全出站白名单 {ExternalHttpConfigurationKeys.DataIntegrationSinkAllowedHosts}。");
        }
    }

    private static async Task EnsureAllowedNetworkAddressesAsync(
        string host,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var cleanHost = host.Trim().TrimEnd('.');
        if (IPAddress.TryParse(cleanHost, out var literalAddress))
        {
            if (!OutboundEndpointConnectionGuard.IsAddressAllowed(
                    literalAddress,
                    configuration,
                    ExternalHttpConfigurationKeys.DataIntegrationSinkAllowedPrivateNetworks))
            {
                throw BlockedAddress(host);
            }

            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(cleanHost, cancellationToken);
        }
        catch (SocketException)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.DataSourceConfigInvalid)
                .WithData("Reason", $"无法解析目标主机地址：{host}。");
        }

        if (addresses.Length == 0 ||
            addresses.Any(address => !OutboundEndpointConnectionGuard.IsAddressAllowed(
                address,
                configuration,
                ExternalHttpConfigurationKeys.DataIntegrationSinkAllowedPrivateNetworks)))
        {
            throw BlockedAddress(host);
        }
    }

    private static BusinessException BlockedAddress(string host)
        => new BusinessException(SwapMidDomainErrorCodes.DataIntegration.DataSourceConfigInvalid)
            .WithData(
                "Reason",
                $"目标主机 {host} 解析到受保护的回环/链路本地或未授权私网地址；如为受控内网节点请在 {ExternalHttpConfigurationKeys.DataIntegrationSinkAllowedPrivateNetworks} 显式配置 CIDR。");
}
