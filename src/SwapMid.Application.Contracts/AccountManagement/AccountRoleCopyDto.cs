using System.ComponentModel.DataAnnotations;

namespace SwapMid.AccountManagement;

/// <summary>角色复制入参：以源角色为模板新建角色（isDefault/isPublic 随源角色，权限清单整份复制）。</summary>
public class AccountRoleCopyDto
{
    /// <summary>新角色名称。</summary>
    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = null!;
}
