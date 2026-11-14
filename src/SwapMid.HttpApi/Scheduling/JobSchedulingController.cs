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

    [HttpGet]
    public Task<List<ScheduledJobDto>> GetListAsync()
        => _service.GetListAsync();

    [HttpPost("set-enabled")]
    public Task SetEnabledAsync(SetScheduledJobEnabledInput input)
        => _service.SetEnabledAsync(input);

    [HttpPost("run")]
    [UnitOfWork(IsDisabled = true)]
    public Task<RunScheduledJobResultDto> RunAsync(RunScheduledJobInput input)
        => _service.RunAsync(input);

    [HttpGet("preview-cron")]
    public Task<List<DateTime>> PreviewCronAsync([FromQuery] string cron)
        => _service.PreviewCronAsync(cron);

    [HttpGet("stats")]
    public Task<ScheduledJobStatsDto> GetStatsAsync()
        => _service.GetStatsAsync();
}
