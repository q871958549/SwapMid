using System;
using System.Collections.Generic;

namespace SwapMid.AccountManagement;

/// <summary>
/// 角色分配弹窗专用的轻量账号行：只含勾选所需字段，不查员工/企业/部门绑定，
/// 角色名经「遍历角色取成员」批量映射，避免逐账号 N+1（1000 账号一次性拉取仍快）。
/// </summary>
public class AccountUserLiteDto
{
    /// <summary>账号唯一标识。</summary>
    public Guid Id { get; set; }
    /// <summary>用户名。</summary>
    public string UserName { get; set; } = null!;
    /// <summary>名。</summary>
    public string? Name { get; set; }
    /// <summary>所属角色名集合。</summary>
    public List<string> RoleNames { get; set; } = new();
}
