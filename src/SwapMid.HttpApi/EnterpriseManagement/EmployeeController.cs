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
[Route("api/v1/employees")]
public class EmployeeController : SwapMidController, IEmployeeAppService
{
    private readonly IEmployeeAppService _service;

    public EmployeeController(IEmployeeAppService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<PagedResultDto<EmployeeDto>> GetListAsync(GetEmployeeListInput input)
        => _service.GetListAsync(input);

    [HttpGet("{id}")]
    public Task<EmployeeDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    [HttpPost]
    public Task<EmployeeDto> CreateAsync(EmployeeCreateUpdateDto input)
        => _service.CreateAsync(input);

    [HttpPut("{id}")]
    public Task<EmployeeDto> UpdateAsync(Guid id, EmployeeCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    [HttpPost("batch-delete")]
    public Task<BatchOperationResultDto> BatchDeleteAsync(BatchIdsInput input)
        => _service.BatchDeleteAsync(input);

    [HttpPost("batch-set-status")]
    public Task<BatchOperationResultDto> BatchSetStatusAsync(BatchSetEmployeeStatusInput input)
        => _service.BatchSetStatusAsync(input);

    [HttpPost("batch-generate-accounts")]
    public Task<BatchAccountResultDto> BatchGenerateAccountsAsync(BatchGenerateAccountsInput input)
        => _service.BatchGenerateAccountsAsync(input);

    [HttpPost("bulk-generate-accounts")]
    public Task<BatchAccountResultDto> BulkGenerateAccountsAsync(BulkGenerateAccountsInput input)
        => _service.BulkGenerateAccountsAsync(input);
}
