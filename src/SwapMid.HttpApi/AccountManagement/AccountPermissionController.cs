using System.Threading.Tasks;
using SwapMid.AccountManagement;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace SwapMid.Controllers.AccountManagement;

[RemoteService]
[Area("app")]
[Route("api/v1/account-roles/{roleName}/permissions")]
public class AccountPermissionController : SwapMidController, IAccountPermissionAppService
{
    private readonly IAccountPermissionAppService _service;

    public AccountPermissionController(IAccountPermissionAppService service)
    {
        _service = service;
    }

    /// <summary>查询指定角色的权限树及已授予项。</summary>
    [HttpGet]
    public Task<AccountRolePermissionDto> GetRolePermissionsAsync(string roleName)
        => _service.GetRolePermissionsAsync(roleName);

    /// <summary>更新指定角色的权限授予状态。</summary>
    [HttpPut]
    public Task UpdateRolePermissionsAsync(string roleName, AccountPermissionUpdateDto input)
        => _service.UpdateRolePermissionsAsync(roleName, input);
}
