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

    /// <summary>分页查询员工档案列表。</summary>
    [HttpGet]
    public Task<PagedResultDto<EmployeeDto>> GetListAsync(GetEmployeeListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单个员工档案详情。</summary>
    [HttpGet("{id}")]
    public Task<EmployeeDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    /// <summary>创建员工档案记录。</summary>
    [HttpPost]
    public Task<EmployeeDto> CreateAsync(EmployeeCreateUpdateDto input)
        => _service.CreateAsync(input);

    /// <summary>更新员工所属组织部门、岗位及在职状态。</summary>
    [HttpPut("{id}")]
    public Task<EmployeeDto> UpdateAsync(Guid id, EmployeeCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>删除指定员工档案。</summary>
    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    /// <summary>按 Id 集合批量删除员工档案。</summary>
    [HttpPost("batch-delete")]
    public Task<BatchOperationResultDto> BatchDeleteAsync(BatchIdsInput input)
        => _service.BatchDeleteAsync(input);

    /// <summary>批量变更选定员工在职/离职状态。</summary>
    [HttpPost("batch-set-status")]
    public Task<BatchOperationResultDto> BatchSetStatusAsync(BatchSetEmployeeStatusInput input)
        => _service.BatchSetStatusAsync(input);

    /// <summary>按员工 Id 列表批量开通对应系统账号。</summary>
    [HttpPost("batch-generate-accounts")]
    public Task<BatchAccountResultDto> BatchGenerateAccountsAsync(BatchGenerateAccountsInput input)
        => _service.BatchGenerateAccountsAsync(input);

    /// <summary>全量/筛选批量为未开通账号的在职员工生成登录账号。</summary>
    [HttpPost("bulk-generate-accounts")]
    public Task<BatchAccountResultDto> BulkGenerateAccountsAsync(BulkGenerateAccountsInput input)
        => _service.BulkGenerateAccountsAsync(input);
}
