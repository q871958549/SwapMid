using System.ComponentModel.DataAnnotations;

namespace SwapMid.AccountManagement;

/// <summary>角色新增/编辑入参。</summary>
public class AccountRoleCreateUpdateDto
{
    /// <summary>角色名称。</summary>
    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = null!;

    /// <summary>是否为默认角色（新用户自动分配）。</summary>
    public bool IsDefault { get; set; }
    /// <summary>是否公开角色。</summary>
    public bool IsPublic { get; set; } = true;
    /// <summary>角色描述（走 ExtraProperties 存储，零迁移，可为空）。</summary>
    [MaxLength(512)]
    public string? Description { get; set; }
    /// <summary>并发戳：编辑时须回传创建/上次查询得到的值，新建时忽略。</summary>
    public string? ConcurrencyStamp { get; set; }
}
