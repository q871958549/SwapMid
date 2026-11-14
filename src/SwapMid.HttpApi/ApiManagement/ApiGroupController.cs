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
[Route("api/v1/api-groups")]
public class ApiGroupController : SwapMidController, IApiGroupAppService
{
    private readonly IApiGroupAppService _service;

    public ApiGroupController(IApiGroupAppService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<PagedResultDto<ApiGroupDto>> GetListAsync(GetApiGroupListInput input)
        => _service.GetListAsync(input);

    [HttpGet("{id}")]
    public Task<ApiGroupDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    [HttpPost]
    public Task<ApiGroupDto> CreateAsync(ApiGroupCreateUpdateDto input)
        => _service.CreateAsync(input);

    [HttpPut("{id}")]
    public Task<ApiGroupDto> UpdateAsync(Guid id, ApiGroupCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);
}
