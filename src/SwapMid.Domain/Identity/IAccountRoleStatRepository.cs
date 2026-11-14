using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.Identity;

/// <summary>
/// 角色聚合统计（成员数 / 权限数），供 <c>AccountManagement.AccountRoleAppService</c> 填充角色列表 DTO。
/// 两个方法均一次性取「全部角色」的聚合结果（角色量级几十~一二百，全量成本可忽略），
/// 由调用方在内存里按角色 Id / 名称做字典 join——<b>刻意不提供单角色查询重载</b>，
/// 逼调用方走批量路径，避免在 GetListAsync 分页循环里逐角色查询（N+1）。
/// </summary>
public interface IAccountRoleStatRepository
{
    /// <summary>
    /// 全部角色的成员数：按 <c>IdentityUserRole.RoleId</c> 分组计数。
    /// </summary>
    /// <returns>key = 角色 Id，value = 成员数；无成员的角色不会出现在结果里（调用方按需 <c>GetValueOrDefault</c>）。</returns>
    Task<Dictionary<Guid, int>> GetMemberCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 全部角色的权限数：按 <c>PermissionGrant.ProviderKey</c> 分组计数（仅 <c>ProviderName == "R"</c> 的角色级授权）。
    /// </summary>
    /// <returns>key = 角色名（<b>PermissionGrant.ProviderKey 存的是角色名，不是角色 Id</b>），value = 已授予权限数。</returns>
    Task<Dictionary<string, int>> GetPermissionCountsAsync(CancellationToken cancellationToken = default);
}
