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

    [HttpGet]
    public Task<PagedResultDto<AccountRoleDto>> GetListAsync(GetAccountRoleListInput input)
        => _service.GetListAsync(input);

    [HttpGet("{id}")]
    public Task<AccountRoleDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    [HttpPost]
    public Task<AccountRoleDto> CreateAsync(AccountRoleCreateUpdateDto input)
        => _service.CreateAsync(input);

    [HttpPut("{id}")]
    public Task<AccountRoleDto> UpdateAsync(Guid id, AccountRoleCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    [HttpPost("{id}/copy")]
    public Task<AccountRoleDto> CopyAsync(Guid id, AccountRoleCopyDto input)
        => _service.CopyAsync(id, input);

    /// <summary>查询角色权限变更历史(从审计日志提取语义化 diff,按时间倒序)。</summary>
    [HttpGet("{roleName}/permission-changes")]
    public Task<PagedResultDto<RolePermissionChangeDto>> GetPermissionChangesAsync(string roleName, PagedResultRequestDto input)
        => _service.GetPermissionChangesAsync(roleName, input);
}
