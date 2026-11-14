using System;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.DataIntegration;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Uow;

namespace SwapMid.Controllers.DataIntegration;

[RemoteService]
[Area("app")]
[Route("api/v1/data-pipelines")]
public class DataPipelineController : SwapMidController, IDataPipelineAppService
{
    private readonly IDataPipelineAppService _service;
    public DataPipelineController(IDataPipelineAppService service) { _service = service; }

    [HttpGet] public Task<PagedResultDto<DataPipelineDto>> GetListAsync(GetDataPipelineListInput input) => _service.GetListAsync(input);
    [HttpGet("{id}")] public Task<DataPipelineDto> GetAsync(Guid id) => _service.GetAsync(id);
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Create)]
    [HttpPost] public Task<DataPipelineDto> CreateAsync(DataPipelineCreateUpdateDto input) => _service.CreateAsync(input);
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Update)]
    [HttpPut("{id}")] public Task<DataPipelineDto> UpdateAsync(Guid id, DataPipelineCreateUpdateDto input) => _service.UpdateAsync(id, input);
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Delete)]
    [HttpDelete("{id}")] public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Execute)]
    [HttpPost("{id}/execute")]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineExecutionDto> ExecuteAsync(
        Guid id,
        [FromQuery, BindRequired] Guid runId,
        CancellationToken cancellationToken = default)
        => _service.ExecuteAsync(id, runId, cancellationToken);

    [NonAction]
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Execute)]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineExecutionDto> ExecuteAsync(
        Guid id,
        Guid runId)
        => _service.ExecuteAsync(id, runId);
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Update)]
    [HttpPut("{id}/activate")]
    [UnitOfWork(IsDisabled = true)]
    public Task ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        _service.ActivateAsync(id, cancellationToken);

    [NonAction]
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Update)]
    [UnitOfWork(IsDisabled = true)]
    public Task ActivateAsync(Guid id) => _service.ActivateAsync(id);
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Update)]
    [HttpPut("{id}/pause")] public Task PauseAsync(Guid id) => _service.PauseAsync(id);
}
