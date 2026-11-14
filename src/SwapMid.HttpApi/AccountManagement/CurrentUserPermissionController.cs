using System;
using System.Threading.Tasks;
using SwapMid.AccountManagement;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace SwapMid.Controllers.AccountManagement;

[RemoteService]
[Area("app")]
[Route("api/v1/my-permissions")]
public class CurrentUserPermissionController : SwapMidController, ICurrentUserPermissionAppService
{
    private readonly ICurrentUserPermissionAppService _service;

    public CurrentUserPermissionController(ICurrentUserPermissionAppService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<CurrentUserPermissionDto> GetAsync()
        => _service.GetAsync();

    /// <summary>查询指定用户当前生效的权限清单(按分组组织,每项附授予来源)。</summary>
    [HttpGet("~/api/v1/account-users/{userId}/effective-permissions")]
    public Task<AccountUserPermissionDto> GetForUserAsync(Guid userId)
        => _service.GetForUserAsync(userId);
}
