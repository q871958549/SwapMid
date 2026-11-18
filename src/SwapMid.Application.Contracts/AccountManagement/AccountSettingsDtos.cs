using System.ComponentModel.DataAnnotations;

namespace SwapMid.AccountManagement;

/// <summary>账号默认设置（初始密码）。</summary>
public class AccountDefaultSettingsDto
{
    /// <summary>新账号初始密码。</summary>
    public string DefaultPassword { get; set; } = string.Empty;
}

/// <summary>账号默认设置更新入参。</summary>
public class UpdateAccountDefaultSettingsDto
{
    /// <summary>新账号初始密码。</summary>
    [Required]
    [MinLength(6)]
    [MaxLength(128)]
    public string DefaultPassword { get; set; } = string.Empty;
}

/// <summary>密码复杂度公开规则（供登录改密、账号表单与个人中心前端提示使用，不含默认密码等敏感配置）。</summary>
public class AccountPasswordPolicyDto
{
    /// <summary>密码最小长度。</summary>
    public int RequiredLength { get; set; }
    /// <summary>密码是否必须包含数字。</summary>
    public bool RequireDigit { get; set; }
    /// <summary>密码是否必须包含小写字母。</summary>
    public bool RequireLowercase { get; set; }
    /// <summary>密码是否必须包含大写字母。</summary>
    public bool RequireUppercase { get; set; }
    /// <summary>密码是否必须包含非字母数字字符（特殊符号）。</summary>
    public bool RequireNonAlphanumeric { get; set; }
}

/// <summary>账号安全设置：初始密码 + 首登强制改密开关 + 密码复杂度规则（即时生效，无需重启）。</summary>
public class AccountSecuritySettingsDto
{
    /// <summary>新账号初始密码。</summary>
    public string DefaultPassword { get; set; } = string.Empty;
    /// <summary>首次登录是否强制修改密码。</summary>
    public bool ForceChangePasswordOnFirstLogin { get; set; }
    /// <summary>密码最小长度。</summary>
    public int PasswordRequiredLength { get; set; }
    /// <summary>密码是否必须包含数字。</summary>
    public bool PasswordRequireDigit { get; set; }
    /// <summary>密码是否必须包含小写字母。</summary>
    public bool PasswordRequireLowercase { get; set; }
    /// <summary>密码是否必须包含大写字母。</summary>
    public bool PasswordRequireUppercase { get; set; }
    /// <summary>密码是否必须包含非字母数字字符（特殊符号）。</summary>
    public bool PasswordRequireNonAlphanumeric { get; set; }
}

/// <summary>账号安全设置更新入参。</summary>
public class UpdateAccountSecuritySettingsDto
{
    /// <summary>新账号初始密码。</summary>
    [Required]
    [MinLength(6)]
    [MaxLength(128)]
    public string DefaultPassword { get; set; } = string.Empty;

    /// <summary>首次登录是否强制修改密码。</summary>
    public bool ForceChangePasswordOnFirstLogin { get; set; }

    /// <summary>密码最小长度。</summary>
    [Range(6, 128)]
    public int PasswordRequiredLength { get; set; } = 6;

    /// <summary>密码是否必须包含数字。</summary>
    public bool PasswordRequireDigit { get; set; } = true;
    /// <summary>密码是否必须包含小写字母。</summary>
    public bool PasswordRequireLowercase { get; set; } = true;
    /// <summary>密码是否必须包含大写字母。</summary>
    public bool PasswordRequireUppercase { get; set; } = true;
    /// <summary>密码是否必须包含非字母数字字符（特殊符号）。</summary>
    public bool PasswordRequireNonAlphanumeric { get; set; } = true;
}
