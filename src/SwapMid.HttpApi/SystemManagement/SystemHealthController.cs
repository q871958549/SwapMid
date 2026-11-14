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

    [HttpGet]
    public Task<SystemHealthDto> GetAsync()
        => _service.GetAsync();
}
