using System;
using System.Linq;
using System.Net;
using SwapMid.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace SwapMid.ApiManagement;

/// <summary>
/// API 网关上游地址安全策略：在外呼建连前执行协议、凭据片段、域名白名单与字面量 IP 网段校验，
/// 配合 <see cref="OutboundEndpointConnectionGuard"/> 的建连阶段 DNS 二次校验共同防御 SSRF 与 DNS Rebinding。
/// </summary>
public static class ApiGatewayUpstreamEndpointPolicy
{
    public static bool TryValidateUpstreamUrl(
        string? upstreamUrl,
        IConfiguration configuration,
        out Uri? validatedUri,
        out string failureCode)
    {
        validatedUri = null;

        if (!Uri.TryCreate(upstreamUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.IdnHost) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            failureCode = "SwapMid:GW:InvalidUpstreamUri";
            return false;
        }

        var allowedHosts = OutboundEndpointConnectionGuard.GetConfiguredValues(
            configuration,
            ExternalHttpConfigurationKeys.ApiGatewayAllowedHosts);
        if (allowedHosts.Count == 0 ||
            !allowedHosts.Any(pattern => OutboundEndpointConnectionGuard.MatchesHost(uri.IdnHost, pattern)))
        {
            failureCode = "SwapMid:GW:UpstreamHostNotAllowed";
            return false;
        }

        if (IPAddress.TryParse(uri.IdnHost, out var literalAddress) &&
            !OutboundEndpointConnectionGuard.IsAddressAllowed(
                literalAddress,
                configuration,
                ExternalHttpConfigurationKeys.ApiGatewayAllowedPrivateNetworks))
        {
            failureCode = "SwapMid:GW:UpstreamNetworkDenied";
            return false;
        }

        validatedUri = uri;
        failureCode = string.Empty;
        return true;
    }
}
