using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

public interface IPipelineExecutionAppService : IApplicationService
{
    Task<PagedResultDto<PipelineExecutionDto>> GetListAsync(GetPipelineExecutionListInput input);
    Task<PipelineExecutionDto> GetAsync(Guid id);
    [UnitOfWork(IsDisabled = true)]
    Task<PipelineExecutionDto> ResolveOutcomeAsync(
        Guid id,
        ResolvePipelineExecutionOutcomeInput input);
    [UnitOfWork(IsDisabled = true)]
    Task<PipelineExecutionDto> ResolveOutcomeAsync(
        Guid id,
        ResolvePipelineExecutionOutcomeInput input,
        CancellationToken cancellationToken = default);

    /// <summary>使用已核验的摘流与目标会话退出证据，受控恢复滞留的运行中执行。</summary>
    [UnitOfWork(IsDisabled = true)]
    Task<PipelineExecutionDto> RecoverRunningAsync(
        Guid id,
        RecoverPipelineExecutionInput input);

    /// <summary>使用已核验的摘流与目标会话退出证据，受控恢复滞留的运行中执行。</summary>
    [UnitOfWork(IsDisabled = true)]
    Task<PipelineExecutionDto> RecoverRunningAsync(
        Guid id,
        RecoverPipelineExecutionInput input,
        CancellationToken cancellationToken = default);

    /// <summary>只读核验目标库同事务回执；仅完整匹配时收敛为成功。</summary>
    [UnitOfWork(IsDisabled = true)]
    Task<PipelineExecutionDto> VerifyTargetReceiptAsync(Guid id);

    /// <summary>只读核验目标库同事务回执；仅完整匹配时收敛为成功。</summary>
    [UnitOfWork(IsDisabled = true)]
    Task<PipelineExecutionDto> VerifyTargetReceiptAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
