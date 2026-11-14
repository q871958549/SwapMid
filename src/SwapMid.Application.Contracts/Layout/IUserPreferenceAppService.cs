using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.Layout;

/// <summary>用户界面偏好（主题 / 字号 / 语言）读写：每账号独立（User 级），跨设备随账号同步。</summary>
public interface IUserPreferenceAppService : IApplicationService
{
    /// <summary>读取当前用户生效的界面偏好。</summary>
    Task<UserPreferenceDto> GetAsync();

    /// <summary>保存当前用户的界面偏好（仅更新入参中非 null 的字段，写 User 级）。</summary>
    Task SaveAsync(SaveUserPreferenceInput input);
}
