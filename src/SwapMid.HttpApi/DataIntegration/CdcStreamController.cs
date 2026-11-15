using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwapMid.DataIntegration;
using SwapMid.Permissions;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Uow;

namespace SwapMid.Controllers.DataIntegration;

[RemoteService]
[Area("app")]
[Route("api/v1/cdc-streams")]
public class CdcStreamController : SwapMidController, ICdcStreamAppService
{
    private readonly ICdcStreamAppService _service;

    public CdcStreamController(ICdcStreamAppService service)
    {
        _service = service;
    }

    /// <summary>分页查询 PostgreSQL CDC 实时流定义列表</summary>
    [HttpGet]
    public Task<PagedResultDto<CdcStreamDto>> GetListAsync(GetCdcStreamListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单个 PostgreSQL CDC 实时流详情</summary>
    [HttpGet("{id}")]
    public Task<CdcStreamDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    /// <summary>新建 PostgreSQL CDC 实时流定义</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Create)]
    [HttpPost]
    public Task<CdcStreamDto> CreateAsync(CdcStreamCreateUpdateDto input)
        => _service.CreateAsync(input);

    /// <summary>更新 PostgreSQL CDC 实时流配置</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Update)]
    [HttpPut("{id}")]
    public Task<CdcStreamDto> UpdateAsync(Guid id, CdcStreamCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>删除 PostgreSQL CDC 实时流定义</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Delete)]
    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    /// <summary>执行 PostgreSQL 逻辑复制前置预检（wal_level、Publication 与 Replication Slot）</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/preflight")]
    public Task<CdcPreflightResultDto> PreflightAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.PreflightAsync(id, cancellationToken);

    /// <summary>激活 CDC 实时流并注册 Hangfire 轮询捕获作业</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPut("{id}/activate")]
    public Task ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.ActivateAsync(id, cancellationToken);

    /// <summary>暂停 CDC 实时流并移除调度轮询作业</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPut("{id}/pause")]
    public Task PauseAsync(Guid id)
        => _service.PauseAsync(id);

    /// <summary>手动立即捕获一批 WAL 变更并投递到下游目标</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/capture")]
    [UnitOfWork(IsDisabled = true)]
    public Task<CdcCaptureResultDto> CaptureNowAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.CaptureNowAsync(id, cancellationToken);

    /// <summary>重置或清空 CDC 流的逻辑复制位点（LSN）</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPut("{id}/checkpoint")]
    public Task<CdcStreamDto> ResetCheckpointAsync(
        Guid id,
        [FromBody] CdcResetCheckpointInput input)
        => _service.ResetCheckpointAsync(id, input);
}
