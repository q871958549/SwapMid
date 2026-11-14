using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace SwapMid.AccountManagement;

/// <summary>
/// 审计用户最小目录。端点面向全部登录用户，因此响应刻意只暴露 Id、显示名与登录名。
/// </summary>
[Authorize]
[DisableAuditing]
[UnitOfWork(false)]
public class AuditUserLookupAppService : ApplicationService, IAuditUserLookupAppService
{
    private readonly IRepository<IdentityUser, Guid> _userRepository;

    public AuditUserLookupAppService(IRepository<IdentityUser, Guid> userRepository)
    {
        _userRepository = userRepository;
    }

    /// <summary>
    /// 按用户 Id 批量解析显示名与登录名；输入先去重，查询始终集合化且只投影显示所需字段。
    /// </summary>
    public async Task<ListResultDto<AuditUserLookupDto>> LookupAsync(
        AuditUserLookupInput input,
        CancellationToken cancellationToken = default)
    {
        var ids = input.Ids.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new ListResultDto<AuditUserLookupDto>([]);
        }

        var query = await _userRepository.GetQueryableAsync();
        var users = await AsyncExecuter.ToListAsync(
            query
                .Where(user => ids.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    user.Name,
                    user.Surname,
                    user.UserName,
                }),
            cancellationToken);

        var usersById = users.ToDictionary(user => user.Id);
        var result = ids
            .Where(usersById.ContainsKey)
            .Select(id =>
            {
                var user = usersById[id];
                var fullName = string.Join(
                    " ",
                    new[] { user.Name, user.Surname }
                        .Where(part => !string.IsNullOrWhiteSpace(part))
                        .Select(part => part!.Trim()));

                return new AuditUserLookupDto
                {
                    Id = id,
                    DisplayName = string.IsNullOrWhiteSpace(fullName)
                        ? user.UserName
                        : fullName,
                    UserName = user.UserName,
                };
            })
            .ToList();

        return new ListResultDto<AuditUserLookupDto>(result);
    }
}
