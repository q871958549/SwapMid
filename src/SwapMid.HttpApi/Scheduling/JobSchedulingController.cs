using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwapMid.Scheduling;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Uow;

namespace SwapMid.Controllers.Scheduling;

[RemoteService]
[Area("app")]
[Route("api/v1/scheduled-jobs")]
public class JobSchedulingController : SwapMidController, IJobSchedulingAppService
{
    private readonly IJobSchedulingAppService _service;

    public JobSchedulingController(IJobSchedulingAppService service)
    {
        _service = service;
    }

    /// <summary>查询所有已注册定时任务的调度状态与元数据清单。</summary>
    [HttpGet]
    public Task<List<ScheduledJobDto>> GetListAsync()
        => _service.GetListAsync();

    /// <summary>启用或停用指定后台定时任务并修改 Cron 表达式。</summary>
    [HttpPost("set-enabled")]
    public Task SetEnabledAsync(SetScheduledJobEnabledInput input)
        => _service.SetEnabledAsync(input);

    /// <summary>手动立即触发或演练（DryRun）执行一次定时任务。</summary>
    [HttpPost("run")]
    [UnitOfWork(IsDisabled = true)]
    public Task<RunScheduledJobResultDto> RunAsync(RunScheduledJobInput input)
        => _service.RunAsync(input);

    /// <summary>解析并预览给定 Cron 表达式未来数次的预计触发时间。</summary>
    [HttpGet("preview-cron")]
    public Task<List<DateTime>> PreviewCronAsync([FromQuery] string cron)
        => _service.PreviewCronAsync(cron);

    /// <summary>获取定时任务整体启用数及近期执行成功/失败统计概览。</summary>
    [HttpGet("stats")]
    public Task<ScheduledJobStatsDto> GetStatsAsync()
        => _service.GetStatsAsync();
}
