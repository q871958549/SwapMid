using System;
using System.Threading.Tasks;
using SwapMid.EnterpriseManagement;
using SwapMid.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace SwapMid.Controllers.EnterpriseManagement;

[RemoteService]
[Area("app")]
[Route("api/v1/positions")]
public class PositionController : SwapMidController, IPositionAppService
{
    private readonly IPositionAppService _service;

    public PositionController(IPositionAppService service)
    {
        _service = service;
    }

    /// <summary>分页查询岗位定义列表。</summary>
    [HttpGet]
    public Task<PagedResultDto<PositionDto>> GetListAsync(GetPositionListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单个岗位详情。</summary>
    [HttpGet("{id}")]
    public Task<PositionDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    /// <summary>创建新岗位。</summary>
    [HttpPost]
    public Task<PositionDto> CreateAsync(PositionCreateUpdateDto input)
        => _service.CreateAsync(input);

    /// <summary>更新岗位名称、编码及所属部门关联。</summary>
    [HttpPut("{id}")]
    public Task<PositionDto> UpdateAsync(Guid id, PositionCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>删除指定岗位。</summary>
    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    /// <summary>按 Id 集合批量删除岗位。</summary>
    [HttpPost("batch-delete")]
    public Task<BatchOperationResultDto> BatchDeleteAsync(BatchIdsInput input)
        => _service.BatchDeleteAsync(input);

    /// <summary>批量启用或停用选定岗位。</summary>
    [HttpPost("batch-set-enabled")]
    public Task<BatchOperationResultDto> BatchSetEnabledAsync(BatchSetEnabledInput input)
        => _service.BatchSetEnabledAsync(input);
}
