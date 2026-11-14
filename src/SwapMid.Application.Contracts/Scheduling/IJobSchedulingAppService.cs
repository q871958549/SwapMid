using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.Scheduling;

public interface IJobSchedulingAppService : IApplicationService
{
    Task<List<ScheduledJobDto>> GetListAsync();

    /// <summary>启用/停用一个定时任务（启用=注册 recurring job，停用=移除，cron 转存草稿）。</summary>
    Task SetEnabledAsync(SetScheduledJobEnabledInput input);

    /// <summary>带参 / 演练执行一次：演练不写库、只发测试收件人。全部受管控任务均已接入统一执行管道，
    /// 无论启用与否都可用本端点立即执行——是「立即执行」唯一入口（旧版 TriggerAsync 已随最后一批
    /// 任务迁移一并移除，见 docs/scheduled-jobs-platform-blueprint.md P2b）。</summary>
    Task<RunScheduledJobResultDto> RunAsync(RunScheduledJobInput input);

    /// <summary>校验 cron 表达式并预览未来 3 次执行时间（UTC；前端按本地时区显示）。表达式非法抛用户友好错误。</summary>
    Task<List<DateTime>> PreviewCronAsync(string cron);

    /// <summary>管控页顶部统计卡：启用数/总数/近 24 小时失败数/当前执行中数。</summary>
    Task<ScheduledJobStatsDto> GetStatsAsync();
}

/// <summary>定时任务管控页顶部统计。</summary>
public class ScheduledJobStatsDto
{
    /// <summary>已启用任务数。</summary>
    public int EnabledCount { get; set; }

    /// <summary>受管控任务总数（ScheduledJobCatalog.All.Count）。</summary>
    public int TotalCount { get; set; }

    /// <summary>近 24 小时内失败的执行次数（按 ScheduledJobExecution.Status=Failed 且 StartedAt 在窗口内统计）。</summary>
    public int Failed24hCount { get; set; }

    /// <summary>近 24 小时统计窗口的精确起点（与 StartedAt 相同的服务器本地时间语义），供钻取执行历史复用。</summary>
    public DateTime Failed24hStartedFrom { get; set; }

    /// <summary>近 24 小时统计快照的结束时刻（闭区间），避免统计与钻取之间新增记录造成口径漂移。</summary>
    public DateTime Failed24hMeasuredAt { get; set; }

    /// <summary>当前处于「执行中」的执行次数（正常情况下应恒为 0 或很快归零；长期非 0 提示卡死，见 F3 僵尸执行收敛）。</summary>
    public int RunningCount { get; set; }
}
