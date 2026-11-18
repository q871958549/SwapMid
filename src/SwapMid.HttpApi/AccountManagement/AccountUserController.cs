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

    /// <summary>分页筛选登录账号列表。</summary>
    [HttpGet]
    public Task<PagedResultDto<AccountUserDto>> GetListAsync(GetAccountUserListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单个登录账号详情。</summary>
    [HttpGet("{id}")]
    public Task<AccountUserDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    /// <summary>创建登录账号。</summary>
    [HttpPost]
    public Task<AccountUserDto> CreateAsync(AccountUserCreateDto input)
        => _service.CreateAsync(input);

    /// <summary>更新登录账号资料与角色。</summary>
    [HttpPut("{id}")]
    public Task<AccountUserDto> UpdateAsync(Guid id, AccountUserUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>启用或停用账号（列表行内切换）。</summary>
    [HttpPost("{id}/set-active")]
    public Task SetActiveAsync(Guid id, bool isActive)
        => _service.SetActiveAsync(id, isActive);

    /// <summary>删除指定登录账号。</summary>
    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    /// <summary>根据在职员工记录批量/单个生成登录账号。</summary>
    [HttpPost("generate-from-employee")]
    public Task<EmployeeAccountProvisionResultDto> GenerateFromEmployeeAsync(GenerateEmployeeAccountInput input)
        => _service.GenerateFromEmployeeAsync(input);

    /// <summary>获取当前允许指派的角色列表。</summary>
    [HttpGet("assignable-roles")]
    public Task<ListResultDto<AccountRoleDto>> GetAssignableRolesAsync()
        => _service.GetAssignableRolesAsync();

    /// <summary>获取用于人员指派/穿梭框展示的轻量账号摘要列表。</summary>
    [HttpGet("for-assignment")]
    public Task<ListResultDto<AccountUserLiteDto>> GetAllForAssignmentAsync()
        => _service.GetAllForAssignmentAsync();

    /// <summary>为选定账号集合批量追加/移除/覆盖角色。</summary>
    [HttpPost("batch-set-roles")]
    public Task<BatchOperationResultDto> BatchSetRolesAsync(BatchSetRolesInput input)
        => _service.BatchSetRolesAsync(input);

    /// <summary>将指定账号密码重置为系统默认密码。</summary>
    [HttpPost("{id}/reset-password")]
    public Task ResetPasswordAsync(Guid id)
        => _service.ResetPasswordAsync(id);

    /// <summary>按角色整体配置绑定成员账号集合。</summary>
    [HttpPost("set-role-members")]
    public Task<BatchOperationResultDto> SetRoleMembersAsync(SetRoleMembersInput input)
        => _service.SetRoleMembersAsync(input);

    /// <summary>查询账号绑定员工的基本信息（账号页点击绑定员工标签用；无绑定返回 null）。</summary>
    [HttpGet("{id}/bound-employee")]
    public Task<BoundEmployeeInfoDto?> GetBoundEmployeeAsync(Guid id)
        => _service.GetBoundEmployeeAsync(id);

    /// <summary>获取可用于账号绑定及资料带出的在职员工选项列表。</summary>
    [HttpGet("employee-options")]
    public Task<ListResultDto<AccountEmployeeOptionDto>> GetEmployeeOptionsAsync()
        => _service.GetEmployeeOptionsAsync();

    /// <summary>获取账号管理页部门筛选下拉选项列表。</summary>
    [HttpGet("department-options")]
    public Task<ListResultDto<AccountDepartmentOptionDto>> GetDepartmentOptionsAsync()
        => _service.GetDepartmentOptionsAsync();
}
