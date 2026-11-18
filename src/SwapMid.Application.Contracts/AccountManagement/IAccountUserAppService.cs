using System;
using System.Threading.Tasks;
using SwapMid.SharedKernel;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SwapMid.AccountManagement;

public interface IAccountUserAppService : IApplicationService
{
    Task<PagedResultDto<AccountUserDto>> GetListAsync(GetAccountUserListInput input);
    Task<AccountUserDto> GetAsync(Guid id);
    Task<AccountUserDto> CreateAsync(AccountUserCreateDto input);
    Task<AccountUserDto> UpdateAsync(Guid id, AccountUserUpdateDto input);

    /// <summary>启用或停用账号（列表行内切换，不走完整编辑）。</summary>
    Task SetActiveAsync(Guid id, bool isActive);

    Task DeleteAsync(Guid id);
    Task<EmployeeAccountProvisionResultDto> GenerateFromEmployeeAsync(GenerateEmployeeAccountInput input);
    Task<ListResultDto<AccountRoleDto>> GetAssignableRolesAsync();

    /// <summary>角色分配弹窗专用：一次性拉取全部账号的轻量信息（id/userName/name/roleNames），
    /// 角色名批量映射，避免逐账号 N+1。</summary>
    Task<ListResultDto<AccountUserLiteDto>> GetAllForAssignmentAsync();

    /// <summary>批量分配角色（追加或覆盖）。逐用户处理，单个失败不影响其余。</summary>
    Task<BatchOperationResultDto> BatchSetRolesAsync(BatchSetRolesInput input);

    /// <summary>重置为初始密码并强制下次登录修改（员工忘记密码时由管理员处理）。</summary>
    Task ResetPasswordAsync(Guid id);

    /// <summary>角色成员全量同步（新增+移除按勾选集合对齐）。逐用户处理，单个失败不影响其余。</summary>
    Task<BatchOperationResultDto> SetRoleMembersAsync(SetRoleMembersInput input);

    /// <summary>获取账号绑定员工的基本信息（按 Employee.UserId 反查；无绑定返回 null）。
    /// 权限语义="账号管理员查看账号的绑定档案"，不要求调用者持有员工域权限。</summary>
    Task<BoundEmployeeInfoDto?> GetBoundEmployeeAsync(Guid id);

    /// <summary>获取账号绑定表单专用在职员工候选选项（权限挂 AccountManagement.Users.Default）。</summary>
    Task<ListResultDto<AccountEmployeeOptionDto>> GetEmployeeOptionsAsync();

    /// <summary>获取账号列表部门筛选专用部门选项（权限挂 AccountManagement.Users.Default）。</summary>
    Task<ListResultDto<AccountDepartmentOptionDto>> GetDepartmentOptionsAsync();
}
