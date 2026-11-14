using System;
using System.Threading.Tasks;
using SwapMid.SharedKernel;
using Volo.Abp.Application.Services;

namespace SwapMid.EnterpriseManagement;

public interface ICompanyDepartmentAppService
    : ICrudAppService<CompanyDepartmentDto, Guid, GetCompanyDepartmentListInput, CompanyDepartmentCreateUpdateDto>
{
    Task<BatchOperationResultDto> BatchDeleteAsync(BatchIdsInput input);

    Task<BatchOperationResultDto> BatchSetEnabledAsync(BatchSetEnabledInput input);
}
