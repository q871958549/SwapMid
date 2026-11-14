using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SwapMid.Scheduling;

/// <summary>
/// 定时任务执行历史查询：供「系统管理→定时任务」页查看每次执行的成败 / 耗时 / 摘要 / 错误。
/// </summary>
public interface IScheduledJobExecutionAppService : IApplicationService
{
    /// <summary>分页查询执行历史（默认按开始时间倒序）。</summary>
    Task<PagedResultDto<ScheduledJobExecutionDto>> GetListAsync(GetScheduledJobExecutionListInput input);
}
