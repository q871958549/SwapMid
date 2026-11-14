using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Permissions;
using SwapMid.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Identity.Settings;
using Volo.Abp.Settings;
using Volo.Abp.SettingManagement;

namespace SwapMid.AccountManagement;

public class AccountSettingsAppService : ApplicationService, IAccountSettingsAppService
{
    private readonly ISettingManager _settingManager;
    private readonly IConfiguration _configuration;

    public AccountSettingsAppService(
        ISettingManager settingManager,
        IConfiguration configuration)
    {
        _settingManager = settingManager;
        _configuration = configuration;
    }

    [Authorize]
    public async Task<AccountDefaultSettingsDto> GetDefaultsAsync()
    {
        var password = await _settingManager.GetOrNullGlobalAsync(SwapMidSettings.AccountDefaultPassword)
            ?? _configuration["Account:DefaultPassword"]
            ?? "SwapMid@123456";
        return new AccountDefaultSettingsDto { DefaultPassword = password };
    }

    [Authorize(SwapMidPermissions.AccountManagement.Users.Update)]
    public async Task<AccountDefaultSettingsDto> UpdateDefaultsAsync(UpdateAccountDefaultSettingsDto input)
    {
        var password = input.DefaultPassword.Trim();
        await ValidateDefaultPasswordAsync(password);

        await _settingManager.SetGlobalAsync(SwapMidSettings.AccountDefaultPassword, password);
        return await GetDefaultsAsync();
    }

    [Authorize]
    public async Task<AccountSecuritySettingsDto> GetSecurityAsync()
    {
        var defaults = await GetDefaultsAsync();
        // ⚠️ 不能读 IOptions<IdentityOptions>.Value：它是进程启动时的静态快照（实测保存后
        // 新请求仍回显旧值，DB 已写新值）。规则一律走 SettingProvider 读当前设置。
        return new AccountSecuritySettingsDto
        {
            DefaultPassword = defaults.DefaultPassword,
            ForceChangePasswordOnFirstLogin = await GetForceChangeAsync(),
            PasswordRequiredLength = await SettingProvider.GetAsync(IdentitySettingNames.Password.RequiredLength, 6),
            PasswordRequireDigit = await SettingProvider.GetAsync(IdentitySettingNames.Password.RequireDigit, true),
            PasswordRequireLowercase = await SettingProvider.GetAsync(IdentitySettingNames.Password.RequireLowercase, true),
            PasswordRequireUppercase = await SettingProvider.GetAsync(IdentitySettingNames.Password.RequireUppercase, true),
            PasswordRequireNonAlphanumeric = await SettingProvider.GetAsync(IdentitySettingNames.Password.RequireNonAlphanumeric, true),
        };
    }

    // 密码规则写入 ABP Identity 全局设置（AbpIdentityOptionsFactory 逐请求映射，改完即时生效）；
    // 初始密码按"新规则"校验后一并保存，避免出现初始密码不满足新规则的自相矛盾配置。
    [Authorize(SwapMidPermissions.AccountManagement.Users.Update)]
    public async Task<AccountSecuritySettingsDto> UpdateSecurityAsync(UpdateAccountSecuritySettingsDto input)
    {
        var password = input.DefaultPassword.Trim();
        ValidateDefaultPassword(
            password,
            input.PasswordRequiredLength,
            input.PasswordRequireDigit,
            input.PasswordRequireLowercase,
            input.PasswordRequireUppercase,
            input.PasswordRequireNonAlphanumeric);

        await _settingManager.SetGlobalAsync(SwapMidSettings.AccountDefaultPassword, password);
        await _settingManager.SetGlobalAsync(
            SwapMidSettings.AccountForceChangePasswordOnFirstLogin,
            input.ForceChangePasswordOnFirstLogin ? "true" : "false");

        await _settingManager.SetGlobalAsync(IdentitySettingNames.Password.RequiredLength, input.PasswordRequiredLength.ToString(CultureInfo.InvariantCulture));
        await _settingManager.SetGlobalAsync(IdentitySettingNames.Password.RequireDigit, input.PasswordRequireDigit ? "true" : "false");
        await _settingManager.SetGlobalAsync(IdentitySettingNames.Password.RequireLowercase, input.PasswordRequireLowercase ? "true" : "false");
        await _settingManager.SetGlobalAsync(IdentitySettingNames.Password.RequireUppercase, input.PasswordRequireUppercase ? "true" : "false");
        await _settingManager.SetGlobalAsync(IdentitySettingNames.Password.RequireNonAlphanumeric, input.PasswordRequireNonAlphanumeric ? "true" : "false");

        // ⚠️ 回显不能重读 GetSecurityAsync：ISettingProvider 在同一请求内有缓存，校验阶段读过的
        // 旧值会盖住刚写入的新值（实测 PUT 6 回显 10）。写入已成功，直接按输入构造回显。
        return new AccountSecuritySettingsDto
        {
            DefaultPassword = password,
            ForceChangePasswordOnFirstLogin = input.ForceChangePasswordOnFirstLogin,
            PasswordRequiredLength = input.PasswordRequiredLength,
            PasswordRequireDigit = input.PasswordRequireDigit,
            PasswordRequireLowercase = input.PasswordRequireLowercase,
            PasswordRequireUppercase = input.PasswordRequireUppercase,
            PasswordRequireNonAlphanumeric = input.PasswordRequireNonAlphanumeric,
        };
    }

    private async Task<bool> GetForceChangeAsync()
        => (await _settingManager.GetOrNullGlobalAsync(SwapMidSettings.AccountForceChangePasswordOnFirstLogin) ?? "true") != "false";

    private async Task ValidateDefaultPasswordAsync(string password)
    {
        ValidateDefaultPassword(
            password,
            await SettingProvider.GetAsync(IdentitySettingNames.Password.RequiredLength, 6),
            await SettingProvider.GetAsync(IdentitySettingNames.Password.RequireDigit, true),
            await SettingProvider.GetAsync(IdentitySettingNames.Password.RequireLowercase, true),
            await SettingProvider.GetAsync(IdentitySettingNames.Password.RequireUppercase, true),
            await SettingProvider.GetAsync(IdentitySettingNames.Password.RequireNonAlphanumeric, true));
    }

    private static void ValidateDefaultPassword(
        string password,
        int requiredLength,
        bool requireDigit,
        bool requireLowercase,
        bool requireUppercase,
        bool requireNonAlphanumeric)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new UserFriendlyException("默认密码不能为空");
        }

        if (password.Length < requiredLength)
        {
            throw new UserFriendlyException($"默认密码至少 {requiredLength} 位");
        }

        if (requireDigit && !password.Any(char.IsDigit))
        {
            throw new UserFriendlyException("默认密码至少包含一位数字");
        }

        if (requireLowercase && !password.Any(char.IsLower))
        {
            throw new UserFriendlyException("默认密码至少包含一位小写字母");
        }

        if (requireUppercase && !password.Any(char.IsUpper))
        {
            throw new UserFriendlyException("默认密码至少包含一位大写字母");
        }

        if (requireNonAlphanumeric && password.All(char.IsLetterOrDigit))
        {
            throw new UserFriendlyException("默认密码至少包含一位特殊字符");
        }
    }
}
