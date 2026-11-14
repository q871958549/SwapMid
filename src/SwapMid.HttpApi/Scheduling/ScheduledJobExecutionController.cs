using System.Threading.Tasks;
using SwapMid.Scheduling;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace SwapMid.Controllers.Scheduling;

/// <summary>定时任务执行历史查询（前端「系统管理→定时任务」历史弹窗用，路由 api/v1/scheduling/executions）。</summary>
[RemoteService]
[Area("app")]
[Route("api/v1/scheduling/executions")]
public class ScheduledJobExecutionController : SwapMidController, IScheduledJobExecutionAppService
{
    private readonly IScheduledJobExecutionAppService _service;

    public ScheduledJobExecutionController(IScheduledJobExecutionAppService service)
    {
        _service = service;
    }

    /// <summary>分页查询执行历史（默认按开始时间倒序）。</summary>
    [HttpGet]
    public Task<PagedResultDto<ScheduledJobExecutionDto>> GetListAsync([FromQuery] GetScheduledJobExecutionListInput input)
        => _service.GetListAsync(input);
}
