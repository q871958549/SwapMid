using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace SwapMid.OpenIddict;

public sealed record ExternalOidcVerifiedIdentity(
    string Subject,
    string? Email,
    bool EmailVerified,
    string? PreferredUsername,
    string? DisplayName);

public interface IExternalOidcCodeExchanger
{
    Task<ExternalOidcVerifiedIdentity?> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string? codeVerifier,
        CancellationToken cancellationToken = default);
}

public sealed class DefaultExternalOidcCodeExchanger : IExternalOidcCodeExchanger
{
    public const string HttpClientName = "ExternalOidc";

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public DefaultExternalOidcCodeExchanger(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ExternalOidcVerifiedIdentity?> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string? codeVerifier,
        CancellationToken cancellationToken = default)
    {
        if (!bool.TryParse(_configuration["AuthServer:ExternalOidc:Enabled"], out var enabled) || !enabled)
        {
            return null;
        }

        var authority = _configuration["AuthServer:ExternalOidc:Authority"]?.Trim().TrimEnd('/');
        var clientId = _configuration["AuthServer:ExternalOidc:ClientId"]?.Trim();
        var clientSecret = _configuration["AuthServer:ExternalOidc:ClientSecret"]?.Trim();
        var requireHttps = !bool.TryParse(_configuration["AuthServer:ExternalOidc:RequireHttpsMetadata"], out var https) || https;

        if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        if (requireHttps && !authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var metadataAddress = $"{authority}/.well-known/openid-configuration";
        var retriever = new HttpDocumentRetriever(httpClient) { RequireHttps = requireHttps };
        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            retriever);

        var oidcConfig = await configManager.GetConfigurationAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(oidcConfig.TokenEndpoint))
        {
            return null;
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
        };
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            form["client_secret"] = clientSecret;
        }
        if (!string.IsNullOrWhiteSpace(codeVerifier))
        {
            form["code_verifier"] = codeVerifier;
        }

        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, oidcConfig.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        using var tokenRes = await httpClient.SendAsync(tokenReq, cancellationToken);
        if (!tokenRes.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await tokenRes.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("id_token", out var idTokenElem) ||
            idTokenElem.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var idToken = idTokenElem.GetString();
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var handler = new JsonWebTokenHandler();
        var validation = await handler.ValidateTokenAsync(
            idToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = oidcConfig.Issuer,
                ValidateAudience = true,
                ValidAudience = clientId,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = oidcConfig.SigningKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            });

        if (!validation.IsValid || validation.ClaimsIdentity is null)
        {
            return null;
        }

        var claims = validation.ClaimsIdentity;
        var sub = claims.FindFirst("sub")?.Value ?? claims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(sub))
        {
            return null;
        }

        var email = claims.FindFirst("email")?.Value ?? claims.FindFirst(ClaimTypes.Email)?.Value;
        var emailVerifiedRaw = claims.FindFirst("email_verified")?.Value;
        var emailVerified = string.IsNullOrWhiteSpace(emailVerifiedRaw)
            || bool.TryParse(emailVerifiedRaw, out var ev) && ev;
        var preferredUsername = claims.FindFirst("preferred_username")?.Value;
        var name = claims.FindFirst("name")?.Value ?? claims.FindFirst(ClaimTypes.Name)?.Value;

        return new ExternalOidcVerifiedIdentity(sub, email, emailVerified, preferredUsername, name);
    }
}
