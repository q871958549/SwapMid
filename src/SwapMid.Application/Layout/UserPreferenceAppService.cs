using System;
using System.Threading.Tasks;
using SwapMid.Settings;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace SwapMid.Layout;

/// <summary>
/// 用户界面偏好（主题 / 字号 / 语言）：每账号独立（User 级设置），跨设备随账号同步。
/// 读走 <see cref="ISettingProvider"/>（与前端 application-configuration 下发的是同一批值）；
/// 写走 <see cref="ISettingManager"/> 的 User 级。入参一律白名单/范围归一，防非法值污染 Setting。
/// 与 <see cref="NavPreferenceAppService"/> 同属「用户界面个性化」，但偏好只写个人、无 admin 写全局分支。
/// </summary>
[Authorize]
public class UserPreferenceAppService : ApplicationService, IUserPreferenceAppService
{
    private readonly ISettingProvider _settingProvider;
    private readonly ISettingManager _settingManager;

    public UserPreferenceAppService(ISettingProvider settingProvider, ISettingManager settingManager)
    {
        _settingProvider = settingProvider;
        _settingManager = settingManager;
    }

    public async Task<UserPreferenceDto> GetAsync()
    {
        var theme = await _settingProvider.GetOrNullAsync(SwapMidSettings.PreferenceTheme);
        var fontSize = await _settingProvider.GetOrNullAsync(SwapMidSettings.PreferenceFontSize);
        var language = await _settingProvider.GetOrNullAsync(SwapMidSettings.PreferenceLanguage);

        return new UserPreferenceDto
        {
            Theme = NormalizeTheme(theme),
            FontSize = NormalizeFontSize(fontSize),
            Language = NormalizeLanguage(language),
        };
    }

    public async Task SaveAsync(SaveUserPreferenceInput input)
    {
        if (input.Theme is not null)
        {
            await _settingManager.SetForCurrentUserAsync(
                SwapMidSettings.PreferenceTheme, NormalizeTheme(input.Theme));
        }

        if (input.FontSize is not null)
        {
            await _settingManager.SetForCurrentUserAsync(
                SwapMidSettings.PreferenceFontSize,
                NormalizeFontSize(input.FontSize.Value.ToString()).ToString());
        }

        if (input.Language is not null)
        {
            await _settingManager.SetForCurrentUserAsync(
                SwapMidSettings.PreferenceLanguage, NormalizeLanguage(input.Language));
        }
    }

    private static string NormalizeTheme(string? value)
        => value is "light" or "dark" or "system" ? value : "system";

    private static int NormalizeFontSize(string? value)
        => int.TryParse(value, out var n) ? Math.Clamp(n, 13, 18) : 16;

    private static string NormalizeLanguage(string? value)
        => value is "zh-Hans" or "zh-Hant" or "en" ? value : "zh-Hans";
}
