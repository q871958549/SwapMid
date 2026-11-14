using System;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.ApiManagement;

/// <summary>
/// 第三方调用方客户端（用于 client_credentials 获取受限 scope=external-integration 的 token）
/// </summary>
public class ThirdPartyClientDto
{
    public Guid Id { get; set; }
    public string ClientId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public DateTime? CreationTime { get; set; }
}

public class CreateThirdPartyClientDto
{
    [StringLength(64)]
    [RegularExpression(@"^[a-zA-Z0-9_-]*$", ErrorMessage = "ClientId 仅允许字母、数字、下划线与连字符")]
    public string? ClientId { get; set; }

    [Required]
    [StringLength(128)]
    public string DisplayName { get; set; } = null!;
}

public class ThirdPartyClientCredentialDto
{
    public Guid Id { get; set; }
    public string ClientId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string ClientSecret { get; set; } = null!;
}
