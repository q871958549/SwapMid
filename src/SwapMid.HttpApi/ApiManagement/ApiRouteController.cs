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

    /// <summary>分页查询动态网关路由列表。</summary>
    [HttpGet]
    public Task<PagedResultDto<ApiRouteDto>> GetListAsync(GetApiRouteListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单条动态网关路由详情。</summary>
    [HttpGet("{id}")]
    public Task<ApiRouteDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    /// <summary>创建动态网关转发路由。</summary>
    [HttpPost]
    public Task<ApiRouteDto> CreateAsync(ApiRouteCreateUpdateDto input)
        => _service.CreateAsync(input);

    /// <summary>更新动态网关转发路由。</summary>
    [HttpPut("{id}")]
    public Task<ApiRouteDto> UpdateAsync(Guid id, ApiRouteCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>删除指定网关路由。</summary>
    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    /// <summary>启用指定网关路由参与转发。</summary>
    [HttpPut("{id}/enable")]
    public Task EnableAsync(Guid id)
        => _service.EnableAsync(id);

    /// <summary>停用指定网关路由转发。</summary>
    [HttpPut("{id}/disable")]
    public Task DisableAsync(Guid id)
        => _service.DisableAsync(id);
}
