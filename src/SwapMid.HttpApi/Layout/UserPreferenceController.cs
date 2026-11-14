using System.Threading.Tasks;
using SwapMid.Layout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace SwapMid.Controllers.Layout;

/// <summary>用户界面偏好（主题 / 字号 / 语言）读写端点。写走 User 级；读也可搭车 application-configuration。</summary>
[RemoteService]
[Area("app")]
[Authorize]
[Route("api/v1/user-preference")]
public class UserPreferenceController : SwapMidController, IUserPreferenceAppService
{
    private readonly IUserPreferenceAppService _service;

    public UserPreferenceController(IUserPreferenceAppService service)
    {
        _service = service;
    }

    /// <summary>读取当前用户生效的界面偏好（主题 / 字号 / 语言）。</summary>
    [HttpGet]
    public Task<UserPreferenceDto> GetAsync()
        => _service.GetAsync();

    /// <summary>保存当前用户的界面偏好（仅更新入参中非 null 的字段，写 User 级）。</summary>
    [HttpPost]
    public Task SaveAsync(SaveUserPreferenceInput input)
        => _service.SaveAsync(input);
}
