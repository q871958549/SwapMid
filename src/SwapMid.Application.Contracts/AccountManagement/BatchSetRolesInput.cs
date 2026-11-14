using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.AccountManagement;

/// <summary>批量分配角色入参。</summary>
public class BatchSetRolesInput
{
    /// <summary>目标账号 ID 集合。</summary>
    [Required]
    [MinLength(1)]
    public List<Guid> UserIds { get; set; } = new();

    /// <summary>目标角色名集合。覆盖模式下传空 = 清空所选用户的全部角色。</summary>
    public List<string> RoleNames { get; set; } = new();

    /// <summary>true = 追加（保留用户现有角色，合并去重）；false = 覆盖为指定集合。</summary>
    public bool Append { get; set; }
}

/// <summary>角色成员全量同步入参（角色页「分配账号」）：UserIds 即该角色的完整成员集合。</summary>
public class SetRoleMembersInput
{
    /// <summary>角色名。</summary>
    [Required]
    [MaxLength(256)]
    public string RoleName { get; set; } = string.Empty;

    /// <summary>勾选的成员集合；不在集合中的现有成员将被移出该角色。</summary>
    public List<Guid> UserIds { get; set; } = new();
}
