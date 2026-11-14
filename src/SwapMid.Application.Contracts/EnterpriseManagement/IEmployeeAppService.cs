using System;
using System.Threading.Tasks;
using SwapMid.SharedKernel;
using Volo.Abp.Application.Services;

namespace SwapMid.EnterpriseManagement;

public interface IEmployeeAppService
    : ICrudAppService<EmployeeDto, Guid, GetEmployeeListInput, EmployeeCreateUpdateDto>
{
    Task<BatchOperationResultDto> BatchDeleteAsync(BatchIdsInput input);

    Task<BatchOperationResultDto> BatchSetStatusAsync(BatchSetEmployeeStatusInput input);

    /// <summary>为勾选的员工批量开通账号（可选指定追加角色，见 <see cref="BatchGenerateAccountsInput"/>）。</summary>
    Task<BatchAccountResultDto> BatchGenerateAccountsAsync(BatchGenerateAccountsInput input);

    /// <summary>按范围一键为"在职且未开通账号"的全部员工开通账号（服务端查询，不受分页限制）。</summary>
    Task<BatchAccountResultDto> BulkGenerateAccountsAsync(BulkGenerateAccountsInput input);
}
