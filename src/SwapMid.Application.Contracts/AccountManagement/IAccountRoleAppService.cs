using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SwapMid.AccountManagement;

public interface IAccountRoleAppService : IApplicationService
{
    Task<PagedResultDto<AccountRoleDto>> GetListAsync(GetAccountRoleListInput input);
    Task<AccountRoleDto> GetAsync(Guid id);
    Task<AccountRoleDto> CreateAsync(AccountRoleCreateUpdateDto input);
    Task<AccountRoleDto> UpdateAsync(Guid id, AccountRoleCreateUpdateDto input);
    Task DeleteAsync(Guid id);
    /// <summary>以 <paramref name="sourceId"/> 角色为模板复制一个新角色：isDefault/isPublic 随源角色、isStatic 恒为 false，
    /// 并整份复制源角色当前已授予的权限。</summary>
    Task<AccountRoleDto> CopyAsync(Guid sourceId, AccountRoleCopyDto input);

    /// <summary>查询角色权限变更历史（从审计日志按 URL+HTTP方法 精确检索该角色的权限 PUT 请求，
    /// 提取写入侧 <see cref="IAccountPermissionAppService.UpdateRolePermissionsAsync"/> 追加的语义化 diff，按时间倒序）。</summary>
    Task<PagedResultDto<RolePermissionChangeDto>> GetPermissionChangesAsync(string roleName, PagedResultRequestDto input);
}
