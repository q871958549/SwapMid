using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;

namespace SwapMid.AccountManagement;

/// <summary>
/// 按账号 Id 批量解析角色名。查询次数不随账号行数增长：一次拉角色表 + 一次 join 当页 UserRoles。
/// 取代列表/分配弹窗里逐账号 <c>IIdentityUserAppService.GetRolesAsync</c>。
/// </summary>
public class AccountUserRoleNamesResolver : ITransientDependency
{
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public AccountUserRoleNamesResolver(
        IRepository<IdentityUser, Guid> userRepository,
        IIdentityRoleRepository roleRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>返回每个请求 Id 的角色名列表（无角色则为空列表，不缺 key）。</summary>
    public async Task<IReadOnlyDictionary<Guid, List<string>>> GetRoleNamesByUserIdsAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        var result = ids.ToDictionary(id => id, _ => new List<string>());
        if (ids.Count == 0)
        {
            return result;
        }

        var roles = await _roleRepository.GetListAsync(cancellationToken: cancellationToken);
        var roleNameById = roles.ToDictionary(role => role.Id, role => role.Name);

        var query = await _userRepository.GetQueryableAsync();
        var pairs = await _asyncExecuter.ToListAsync(
            query.Where(user => ids.Contains(user.Id))
                .SelectMany(
                    user => user.Roles,
                    (user, userRole) => new { UserId = user.Id, userRole.RoleId }),
            cancellationToken);

        foreach (var pair in pairs)
        {
            if (!roleNameById.TryGetValue(pair.RoleId, out var name))
            {
                continue;
            }

            result[pair.UserId].Add(name);
        }

        return result;
    }
}
