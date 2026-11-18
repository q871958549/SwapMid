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

    /// <summary>分页查询数据管道定义列表。</summary>
    [HttpGet] public Task<PagedResultDto<DataPipelineDto>> GetListAsync(GetDataPipelineListInput input) => _service.GetListAsync(input);
    /// <summary>获取单条数据管道详情。</summary>
    [HttpGet("{id}")] public Task<DataPipelineDto> GetAsync(Guid id) => _service.GetAsync(id);
    /// <summary>创建数据管道草稿（需经 Schema 预检及激活方可执行）。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Create)]
    [HttpPost] public Task<DataPipelineDto> CreateAsync(DataPipelineCreateUpdateDto input) => _service.CreateAsync(input);
    /// <summary>更新数据管道配置（字段映射、增量模式及 Cron 调度）。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Update)]
    [HttpPut("{id}")] public Task<DataPipelineDto> UpdateAsync(Guid id, DataPipelineCreateUpdateDto input) => _service.UpdateAsync(id, input);
    /// <summary>删除指定数据管道。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Delete)]
    [HttpDelete("{id}")] public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);
    /// <summary>按确定性 RunId 立即幂等触发一次 ETL 数据管道执行。</summary>
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
    /// <summary>激活数据管道（建立 Target Receipt v1 / Sink 就绪状态并注册定时调度）。</summary>
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
    /// <summary>暂停数据管道并移除 Hangfire 定时轮询。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Update)]
    [HttpPut("{id}/pause")] public Task PauseAsync(Guid id) => _service.PauseAsync(id);
}
