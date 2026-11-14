using System.Threading.Tasks;
using SwapMid.AccountManagement;
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

    [HttpGet("defaults")]
    public Task<AccountDefaultSettingsDto> GetDefaultsAsync()
        => _service.GetDefaultsAsync();

    [HttpPut("defaults")]
    public Task<AccountDefaultSettingsDto> UpdateDefaultsAsync(UpdateAccountDefaultSettingsDto input)
        => _service.UpdateDefaultsAsync(input);

    [HttpGet("security")]
    public Task<AccountSecuritySettingsDto> GetSecurityAsync()
        => _service.GetSecurityAsync();

    [HttpPut("security")]
    public Task<AccountSecuritySettingsDto> UpdateSecurityAsync(UpdateAccountSecuritySettingsDto input)
        => _service.UpdateSecurityAsync(input);
}
