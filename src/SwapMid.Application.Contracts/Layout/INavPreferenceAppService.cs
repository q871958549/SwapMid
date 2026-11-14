using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.Layout;

public interface INavPreferenceAppService : IApplicationService
{
    Task<NavOrderDto> GetAsync();

    /// <summary>保存导航顺序：admin → 全局默认；普通用户 → 个人。</summary>
    Task SaveAsync(SaveNavOrderInput input);

    /// <summary>重置：admin → 清全局默认；普通用户 → 清个人覆盖。</summary>
    Task ResetAsync();
}
