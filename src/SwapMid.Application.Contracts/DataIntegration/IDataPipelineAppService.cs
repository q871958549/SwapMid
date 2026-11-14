using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

public interface IDataPipelineAppService
    : ICrudAppService<DataPipelineDto, Guid, GetDataPipelineListInput, DataPipelineCreateUpdateDto>
{
    [UnitOfWork(IsDisabled = true)]
    Task<PipelineExecutionDto> ExecuteAsync(Guid id, Guid runId);

    [UnitOfWork(IsDisabled = true)]
    Task<PipelineExecutionDto> ExecuteAsync(
        Guid id,
        Guid runId,
        CancellationToken cancellationToken = default);
    [UnitOfWork(IsDisabled = true)]
    Task ActivateAsync(Guid id);
    [UnitOfWork(IsDisabled = true)]
    Task ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default);
    Task PauseAsync(Guid id);
}
