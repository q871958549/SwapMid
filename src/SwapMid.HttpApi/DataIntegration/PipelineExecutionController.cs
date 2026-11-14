using System;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.DataIntegration;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Http;
using Volo.Abp.Uow;

namespace SwapMid.Controllers.DataIntegration;

[RemoteService]
[Area("app")]
[Route("api/v1/pipeline-executions")]
public class PipelineExecutionController : SwapMidController, IPipelineExecutionAppService
{
    private readonly IPipelineExecutionAppService _service;
    public PipelineExecutionController(IPipelineExecutionAppService service) { _service = service; }

    [HttpGet] public Task<PagedResultDto<PipelineExecutionDto>> GetListAsync(GetPipelineExecutionListInput input) => _service.GetListAsync(input);
    [HttpGet("{id}")] public Task<PipelineExecutionDto> GetAsync(Guid id) => _service.GetAsync(id);

    [Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.ResolveOutcome)]
    [HttpPost("{id}/resolve-outcome")]
    [ProducesResponseType(
        typeof(PipelineExecutionDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(RemoteServiceErrorResponse),
        StatusCodes.Status409Conflict,
        "application/json")]
    [ProducesResponseType(
        typeof(RemoteServiceErrorResponse),
        StatusCodes.Status503ServiceUnavailable,
        "application/json")]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineExecutionDto> ResolveOutcomeAsync(
        Guid id,
        [FromBody] ResolvePipelineExecutionOutcomeInput input,
        CancellationToken cancellationToken = default)
        => _service.ResolveOutcomeAsync(id, input, cancellationToken);

    [NonAction]
    [Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.ResolveOutcome)]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineExecutionDto> ResolveOutcomeAsync(
        Guid id,
        ResolvePipelineExecutionOutcomeInput input)
        => _service.ResolveOutcomeAsync(id, input);

    /// <summary>使用已核验的摘流与目标会话退出证据，受控恢复滞留的运行中执行。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.RecoverRunning)]
    [HttpPost("{id}/recover-running")]
    [ProducesResponseType(
        typeof(PipelineExecutionDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(RemoteServiceErrorResponse),
        StatusCodes.Status409Conflict,
        "application/json")]
    [ProducesResponseType(
        typeof(RemoteServiceErrorResponse),
        StatusCodes.Status503ServiceUnavailable,
        "application/json")]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineExecutionDto> RecoverRunningAsync(
        Guid id,
        [FromBody] RecoverPipelineExecutionInput input,
        CancellationToken cancellationToken = default)
        => _service.RecoverRunningAsync(id, input, cancellationToken);

    /// <summary>使用已核验的摘流与目标会话退出证据，受控恢复滞留的运行中执行。</summary>
    [NonAction]
    [Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.RecoverRunning)]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineExecutionDto> RecoverRunningAsync(
        Guid id,
        RecoverPipelineExecutionInput input)
        => _service.RecoverRunningAsync(id, input);

    /// <summary>只读核验目标库同事务回执；仅完整匹配时收敛为成功。</summary>
    [HttpPost("{id}/verify-target-receipt")]
    [Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.VerifyTargetReceipt)]
    [ProducesResponseType(
        typeof(PipelineExecutionDto),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(RemoteServiceErrorResponse),
        StatusCodes.Status409Conflict,
        "application/json")]
    [ProducesResponseType(
        typeof(RemoteServiceErrorResponse),
        StatusCodes.Status503ServiceUnavailable,
        "application/json")]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineExecutionDto> VerifyTargetReceiptAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.VerifyTargetReceiptAsync(id, cancellationToken);

    /// <summary>只读核验目标库同事务回执；仅完整匹配时收敛为成功。</summary>
    [NonAction]
    [Authorize(SwapMidPermissions.DataIntegration.PipelineExecutions.VerifyTargetReceipt)]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineExecutionDto> VerifyTargetReceiptAsync(Guid id)
        => _service.VerifyTargetReceiptAsync(id);
}
