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

    /// <summary>读取当前登录用户的导航菜单排序偏好及管理员标志。</summary>
    [HttpGet]
    public Task<NavOrderDto> GetAsync()
        => _service.GetAsync();

    /// <summary>保存导航菜单自定义排序（管理员写全局默认，其余用户写个人配置）。</summary>
    [HttpPost]
    public Task SaveAsync(SaveNavOrderInput input)
        => _service.SaveAsync(input);

    /// <summary>重置当前用户或全局默认导航排序。</summary>
    [HttpPost("reset")]
    public Task ResetAsync()
        => _service.ResetAsync();
}
