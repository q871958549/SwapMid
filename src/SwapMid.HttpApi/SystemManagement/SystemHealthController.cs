using System.Threading.Tasks;
using SwapMid.SystemManagement;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace SwapMid.Controllers.SystemManagement;

[RemoteService]
[Area("app")]
[Route("api/v1/system-health")]
public class SystemHealthController : SwapMidController, ISystemHealthAppService
{
    private readonly ISystemHealthAppService _service;

    public SystemHealthController(ISystemHealthAppService service)
    {
        _service = service;
    }

    /// <summary>执行并返回平台核心组件（数据库 / Redis / 队列 / CDC / ETL / 网关指标）系统健康体检快照。</summary>
    [HttpGet]
    public Task<SystemHealthDto> GetAsync()
        => _service.GetAsync();
}
