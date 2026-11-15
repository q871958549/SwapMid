namespace SwapMid.AccountManagement;

/// <summary>登录页外部身份提供商公开元数据（绝不包含 ClientSecret）。</summary>
public sealed class ExternalAuthProvidersDto
{
    /// <summary>是否已启用并完整配置外部 OIDC 单点登录。</summary>
    public bool ExternalOidcEnabled { get; init; }

    /// <summary>外部 OIDC 提供方展示名称（如 Enterprise SSO / Keycloak / Entra ID）。</summary>
    public string DisplayName { get; init; } = "Enterprise SSO";

    /// <summary>OIDC Issuer / Authority 根地址（仅启用时返回）。</summary>
    public string? Authority { get; init; }

    /// <summary>公开 OIDC ClientId（仅启用时返回）。</summary>
    public string? ClientId { get; init; }

    /// <summary>可选显式覆盖的授权端点 URL；为空时由 BFF 按 OIDC Discovery 自动发现。</summary>
    public string? AuthorizationEndpoint { get; init; }
}
