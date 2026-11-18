using System;
using System.Threading.Tasks;
using SwapMid.AccountManagement;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace SwapMid.Controllers.AccountManagement;

[RemoteService]
[Area("app")]
[Route("api/v1/account-roles")]
public class AccountRoleController : SwapMidController, IAccountRoleAppService
{
    private readonly IAccountRoleAppService _service;

    public AccountRoleController(IAccountRoleAppService service)
    {
        _service = service;
    }

    /// <summary>分页查询角色列表。</summary>
    [HttpGet]
    public Task<PagedResultDto<AccountRoleDto>> GetListAsync(GetAccountRoleListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单个角色详情。</summary>
    [HttpGet("{id}")]
    public Task<AccountRoleDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    /// <summary>创建新角色。</summary>
    [HttpPost]
    public Task<AccountRoleDto> CreateAsync(AccountRoleCreateUpdateDto input)
        => _service.CreateAsync(input);

    /// <summary>更新角色基本信息与描述。</summary>
    [HttpPut("{id}")]
    public Task<AccountRoleDto> UpdateAsync(Guid id, AccountRoleCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>删除指定角色。</summary>
    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    /// <summary>复制已有角色及其权限授予集合生成新角色。</summary>
    [HttpPost("{id}/copy")]
    public Task<AccountRoleDto> CopyAsync(Guid id, AccountRoleCopyDto input)
        => _service.CopyAsync(id, input);

    /// <summary>查询角色权限变更历史(从审计日志提取语义化 diff,按时间倒序)。</summary>
    [HttpGet("{roleName}/permission-changes")]
    public Task<PagedResultDto<RolePermissionChangeDto>> GetPermissionChangesAsync(string roleName, PagedResultRequestDto input)
        => _service.GetPermissionChangesAsync(roleName, input);
}
