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

    /// <summary>分页查询 API 路由分组列表。</summary>
    [HttpGet]
    public Task<PagedResultDto<ApiGroupDto>> GetListAsync(GetApiGroupListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单个 API 路由分组详情。</summary>
    [HttpGet("{id}")]
    public Task<ApiGroupDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    /// <summary>创建 API 路由分组。</summary>
    [HttpPost]
    public Task<ApiGroupDto> CreateAsync(ApiGroupCreateUpdateDto input)
        => _service.CreateAsync(input);

    /// <summary>更新 API 路由分组配置。</summary>
    [HttpPut("{id}")]
    public Task<ApiGroupDto> UpdateAsync(Guid id, ApiGroupCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>删除指定 API 路由分组。</summary>
    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);
}
