using System;
using System.Threading.Tasks;
using SwapMid.AccountManagement;
using SwapMid.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace SwapMid.Controllers.AccountManagement;

[RemoteService]
[Area("app")]
[Route("api/v1/account-users")]
public class AccountUserController : SwapMidController, IAccountUserAppService
{
    private readonly IAccountUserAppService _service;

    public AccountUserController(IAccountUserAppService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<PagedResultDto<AccountUserDto>> GetListAsync(GetAccountUserListInput input)
        => _service.GetListAsync(input);

    [HttpGet("{id}")]
    public Task<AccountUserDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    [HttpPost]
    public Task<AccountUserDto> CreateAsync(AccountUserCreateDto input)
        => _service.CreateAsync(input);

    [HttpPut("{id}")]
    public Task<AccountUserDto> UpdateAsync(Guid id, AccountUserUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>启用或停用账号（列表行内切换）。</summary>
    [HttpPost("{id}/set-active")]
    public Task SetActiveAsync(Guid id, bool isActive)
        => _service.SetActiveAsync(id, isActive);

    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    [HttpPost("generate-from-employee")]
    public Task<EmployeeAccountProvisionResultDto> GenerateFromEmployeeAsync(GenerateEmployeeAccountInput input)
        => _service.GenerateFromEmployeeAsync(input);

    [HttpGet("assignable-roles")]
    public Task<ListResultDto<AccountRoleDto>> GetAssignableRolesAsync()
        => _service.GetAssignableRolesAsync();

    [HttpGet("for-assignment")]
    public Task<ListResultDto<AccountUserLiteDto>> GetAllForAssignmentAsync()
        => _service.GetAllForAssignmentAsync();

    [HttpPost("batch-set-roles")]
    public Task<BatchOperationResultDto> BatchSetRolesAsync(BatchSetRolesInput input)
        => _service.BatchSetRolesAsync(input);

    [HttpPost("{id}/reset-password")]
    public Task ResetPasswordAsync(Guid id)
        => _service.ResetPasswordAsync(id);

    [HttpPost("set-role-members")]
    public Task<BatchOperationResultDto> SetRoleMembersAsync(SetRoleMembersInput input)
        => _service.SetRoleMembersAsync(input);

    /// <summary>查询账号绑定员工的基本信息（账号页点击绑定员工标签用；无绑定返回 null）。</summary>
    [HttpGet("{id}/bound-employee")]
    public Task<BoundEmployeeInfoDto?> GetBoundEmployeeAsync(Guid id)
        => _service.GetBoundEmployeeAsync(id);
}
