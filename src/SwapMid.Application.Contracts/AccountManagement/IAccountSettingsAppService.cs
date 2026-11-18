using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.AccountManagement;

public interface IAccountSettingsAppService : IApplicationService
{
    Task<AccountDefaultSettingsDto> GetDefaultsAsync();
    Task<AccountDefaultSettingsDto> UpdateDefaultsAsync(UpdateAccountDefaultSettingsDto input);
    Task<AccountPasswordPolicyDto> GetPasswordPolicyAsync();
    Task<AccountSecuritySettingsDto> GetSecurityAsync();
    Task<AccountSecuritySettingsDto> UpdateSecurityAsync(UpdateAccountSecuritySettingsDto input);
}
