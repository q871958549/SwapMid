using System;
using System.Threading.Tasks;
using SwapMid.SharedKernel;
using Volo.Abp.Application.Services;

namespace SwapMid.EnterpriseManagement;

public interface IPositionAppService
    : ICrudAppService<PositionDto, Guid, GetPositionListInput, PositionCreateUpdateDto>
{
    Task<BatchOperationResultDto> BatchDeleteAsync(BatchIdsInput input);

    Task<BatchOperationResultDto> BatchSetEnabledAsync(BatchSetEnabledInput input);
}
