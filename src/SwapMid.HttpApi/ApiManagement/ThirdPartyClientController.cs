using System.Collections.Generic;
using System.Threading.Tasks;
using SwapMid.ApiManagement;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace SwapMid.Controllers.ApiManagement;

/// <summary>
/// 第三方应用/凭据管理（<c>api/v1/third-party-clients</c>）。仅做路由代理，业务在
/// <see cref="IThirdPartyClientAppService"/>。权限在 AppService 上声明（ApiManagement.ThirdPartyClients）。
/// </summary>
[RemoteService]
[Area("app")]
[Route("api/v1/third-party-clients")]
public class ThirdPartyClientController : SwapMidController, IThirdPartyClientAppService
{
    private readonly IThirdPartyClientAppService _service;

    public ThirdPartyClientController(IThirdPartyClientAppService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<List<ThirdPartyClientDto>> GetListAsync() => _service.GetListAsync();

    [HttpPost]
    public Task<ThirdPartyClientCredentialDto> CreateAsync(CreateThirdPartyClientDto input)
        => _service.CreateAsync(input);

    [HttpPost("{clientId}/reset-secret")]
    public Task<ThirdPartyClientCredentialDto> ResetSecretAsync(string clientId)
        => _service.ResetSecretAsync(clientId);

    [HttpDelete("{clientId}")]
    public Task DeleteAsync(string clientId) => _service.DeleteAsync(clientId);
}
