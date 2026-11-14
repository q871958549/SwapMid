using System;
using System.Threading.Tasks;
using SwapMid.ApiManagement;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace SwapMid.Controllers.ApiManagement;

[RemoteService]
[Area("app")]
[Route("api/v1/api-routes")]
public class ApiRouteController : SwapMidController, IApiRouteAppService
{
    private readonly IApiRouteAppService _service;

    public ApiRouteController(IApiRouteAppService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<PagedResultDto<ApiRouteDto>> GetListAsync(GetApiRouteListInput input)
        => _service.GetListAsync(input);

    [HttpGet("{id}")]
    public Task<ApiRouteDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    [HttpPost]
    public Task<ApiRouteDto> CreateAsync(ApiRouteCreateUpdateDto input)
        => _service.CreateAsync(input);

    [HttpPut("{id}")]
    public Task<ApiRouteDto> UpdateAsync(Guid id, ApiRouteCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    [HttpPut("{id}/enable")]
    public Task EnableAsync(Guid id)
        => _service.EnableAsync(id);

    [HttpPut("{id}/disable")]
    public Task DisableAsync(Guid id)
        => _service.DisableAsync(id);
}
