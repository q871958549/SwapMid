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

    [HttpGet]
    public Task<PagedResultDto<PositionDto>> GetListAsync(GetPositionListInput input)
        => _service.GetListAsync(input);

    [HttpGet("{id}")]
    public Task<PositionDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    [HttpPost]
    public Task<PositionDto> CreateAsync(PositionCreateUpdateDto input)
        => _service.CreateAsync(input);

    [HttpPut("{id}")]
    public Task<PositionDto> UpdateAsync(Guid id, PositionCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    [HttpPost("batch-delete")]
    public Task<BatchOperationResultDto> BatchDeleteAsync(BatchIdsInput input)
        => _service.BatchDeleteAsync(input);

    [HttpPost("batch-set-enabled")]
    public Task<BatchOperationResultDto> BatchSetEnabledAsync(BatchSetEnabledInput input)
        => _service.BatchSetEnabledAsync(input);
}
