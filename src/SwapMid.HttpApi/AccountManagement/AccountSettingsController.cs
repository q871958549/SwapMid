using System.Threading.Tasks;
using SwapMid.AccountManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace SwapMid.Controllers.AccountManagement;

[RemoteService]
[Area("app")]
[Route("api/v1/account-management/settings")]
public class AccountSettingsController : SwapMidController, IAccountSettingsAppService
{
    private readonly IAccountSettingsAppService _service;

    public AccountSettingsController(IAccountSettingsAppService service)
    {
        _service = service;
    }

    /// <summary>获取账号默认初始化策略配置。</summary>
    [HttpGet("defaults")]
    public Task<AccountDefaultSettingsDto> GetDefaultsAsync()
        => _service.GetDefaultsAsync();

    /// <summary>更新账号默认初始化策略配置。</summary>
    [HttpPut("defaults")]
    public Task<AccountDefaultSettingsDto> UpdateDefaultsAsync(UpdateAccountDefaultSettingsDto input)
        => _service.UpdateDefaultsAsync(input);

    /// <summary>匿名读取当前密码复杂度公开规则（供登录改密与账号表单提示使用，不含敏感默认密码）。</summary>
    [AllowAnonymous]
    [HttpGet("/api/v1/account-management/password-policy")]
    public Task<AccountPasswordPolicyDto> GetPasswordPolicyAsync()
        => _service.GetPasswordPolicyAsync();

    /// <summary>获取账号密码复杂度与锁定安全策略配置。</summary>
    [HttpGet("security")]
    public Task<AccountSecuritySettingsDto> GetSecurityAsync()
        => _service.GetSecurityAsync();

    /// <summary>更新账号密码复杂度与锁定安全策略配置。</summary>
    [HttpPut("security")]
    public Task<AccountSecuritySettingsDto> UpdateSecurityAsync(UpdateAccountSecuritySettingsDto input)
        => _service.UpdateSecurityAsync(input);
}
