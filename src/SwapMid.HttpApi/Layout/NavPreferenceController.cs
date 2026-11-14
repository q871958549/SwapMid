using System.Threading.Tasks;
using SwapMid.Layout;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace SwapMid.Controllers.Layout;

[RemoteService]
[Area("app")]
[Route("api/v1/nav-preference")]
public class NavPreferenceController : SwapMidController, INavPreferenceAppService
{
    private readonly INavPreferenceAppService _service;

    public NavPreferenceController(INavPreferenceAppService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<NavOrderDto> GetAsync()
        => _service.GetAsync();

    [HttpPost]
    public Task SaveAsync(SaveNavOrderInput input)
        => _service.SaveAsync(input);

    [HttpPost("reset")]
    public Task ResetAsync()
        => _service.ResetAsync();
}
