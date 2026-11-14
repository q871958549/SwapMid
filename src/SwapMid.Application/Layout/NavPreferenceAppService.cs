using System.Threading.Tasks;
using SwapMid.Settings;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.Users;

namespace SwapMid.Layout;

/// <summary>
/// 导航栏顺序偏好：每账号独立（User 级设置）；admin 改动写入 Global = 全局默认顺序。
/// </summary>
[Authorize]
public class NavPreferenceAppService : ApplicationService, INavPreferenceAppService
{
    private const string AdminRole = "admin";

    private readonly ISettingProvider _settingProvider;
    private readonly ISettingManager _settingManager;

    public NavPreferenceAppService(ISettingProvider settingProvider, ISettingManager settingManager)
    {
        _settingProvider = settingProvider;
        _settingManager = settingManager;
    }

    public async Task<NavOrderDto> GetAsync()
    {
        var value = await _settingProvider.GetOrNullAsync(SwapMidSettings.LayoutNavOrder);
        return new NavOrderDto { Value = value, IsAdmin = CurrentUser.IsInRole(AdminRole) };
    }

    public async Task SaveAsync(SaveNavOrderInput input)
    {
        var value = input.Value ?? string.Empty;
        if (CurrentUser.IsInRole(AdminRole))
        {
            await _settingManager.SetGlobalAsync(SwapMidSettings.LayoutNavOrder, value);
        }
        else
        {
            await _settingManager.SetForCurrentUserAsync(SwapMidSettings.LayoutNavOrder, value);
        }
    }

    public async Task ResetAsync()
    {
        // 传 null 删除该级设置：普通用户回落到全局默认，admin 回落到应用默认。
        if (CurrentUser.IsInRole(AdminRole))
        {
            await _settingManager.SetGlobalAsync(SwapMidSettings.LayoutNavOrder, null!);
        }
        else
        {
            await _settingManager.SetForCurrentUserAsync(SwapMidSettings.LayoutNavOrder, null!);
        }
    }
}
