using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.SystemManagement;

/// <summary>
/// 系统体检（只读）：查 Prometheus 把机器/业务指标翻译成中文健康状态。
/// Prometheus 连不上时优雅降级为「未知」，绝不抛异常。
/// </summary>
public interface ISystemHealthAppService : IApplicationService
{
    Task<SystemHealthDto> GetAsync();
}
