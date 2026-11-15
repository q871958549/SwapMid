using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.AccountManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Volo.Abp;

namespace SwapMid.Controllers.AccountManagement;

[RemoteService]
[Area("app")]
[AllowAnonymous]
[Route("api/v1/auth-providers")]
public class AuthProviderController : SwapMidController
{
    public const string ExternalOidcHttpClientName = "ExternalOidc";

    private static readonly ConcurrentDictionary<string, (string Endpoint, DateTimeOffset ExpiresAt)> DiscoveryCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public AuthProviderController(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>查询登录页可用的外部身份认证提供方公开配置（不含客户端密钥）。</summary>
    [HttpGet]
    public async Task<ExternalAuthProvidersDto> GetAsync(CancellationToken cancellationToken)
    {
        var enabled = bool.TryParse(_configuration["AuthServer:ExternalOidc:Enabled"], out var flag) && flag;
        var authority = _configuration["AuthServer:ExternalOidc:Authority"]?.Trim();
        var clientId = _configuration["AuthServer:ExternalOidc:ClientId"]?.Trim();
        var displayName = _configuration["AuthServer:ExternalOidc:DisplayName"]?.Trim();
        var authorizationEndpoint = _configuration["AuthServer:ExternalOidc:AuthorizationEndpoint"]?.Trim();

        var isReady = enabled
            && !string.IsNullOrWhiteSpace(authority)
            && Uri.TryCreate(authority, UriKind.Absolute, out _)
            && !string.IsNullOrWhiteSpace(clientId);

        if (!isReady)
        {
            return new ExternalAuthProvidersDto
            {
                ExternalOidcEnabled = false,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Enterprise SSO" : displayName,
            };
        }

        var normalizedAuthority = authority!.TrimEnd('/');
        var resolvedAuthorizationEndpoint = string.IsNullOrWhiteSpace(authorizationEndpoint)
            ? await TryDiscoverAuthorizationEndpointAsync(normalizedAuthority, cancellationToken)
            : authorizationEndpoint;

        return new ExternalAuthProvidersDto
        {
            ExternalOidcEnabled = true,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Enterprise SSO" : displayName,
            Authority = normalizedAuthority,
            ClientId = clientId,
            AuthorizationEndpoint = resolvedAuthorizationEndpoint,
        };
    }

    private async Task<string?> TryDiscoverAuthorizationEndpointAsync(
        string normalizedAuthority,
        CancellationToken cancellationToken)
    {
        if (DiscoveryCache.TryGetValue(normalizedAuthority, out var cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Endpoint;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(ExternalOidcHttpClientName);
            if (client is null)
            {
                return null;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var response = await client.GetAsync(
                $"{normalizedAuthority}/.well-known/openid-configuration",
                cts.Token);
            if (response is not { IsSuccessStatusCode: true, Content: not null })
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            if (doc.RootElement.TryGetProperty("authorization_endpoint", out var elem)
                && elem.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(elem.GetString()))
            {
                var endpoint = elem.GetString()!;
                DiscoveryCache[normalizedAuthority] = (endpoint, DateTimeOffset.UtcNow.AddMinutes(5));
                return endpoint;
            }
        }
        catch
        {
            // Explicit AuthServer:ExternalOidc:AuthorizationEndpoint overrides unreachable discovery endpoints.
        }

        return null;
    }
}
