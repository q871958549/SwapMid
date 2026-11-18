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
[Route("api/v1/departments")]
public class CompanyDepartmentController : SwapMidController, ICompanyDepartmentAppService
{
    private readonly ICompanyDepartmentAppService _service;

    public CompanyDepartmentController(ICompanyDepartmentAppService service)
    {
        _service = service;
    }

    /// <summary>分页查询组织部门树及列表。</summary>
    [HttpGet]
    public Task<PagedResultDto<CompanyDepartmentDto>> GetListAsync(GetCompanyDepartmentListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单个部门详情。</summary>
    [HttpGet("{id}")]
    public Task<CompanyDepartmentDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    /// <summary>创建新部门。</summary>
    [HttpPost]
    public Task<CompanyDepartmentDto> CreateAsync(CompanyDepartmentCreateUpdateDto input)
        => _service.CreateAsync(input);

    /// <summary>更新部门名称、上级或排序设置。</summary>
    [HttpPut("{id}")]
    public Task<CompanyDepartmentDto> UpdateAsync(Guid id, CompanyDepartmentCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>删除指定部门。</summary>
    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    /// <summary>按 Id 集合批量删除部门。</summary>
    [HttpPost("batch-delete")]
    public Task<BatchOperationResultDto> BatchDeleteAsync(BatchIdsInput input)
        => _service.BatchDeleteAsync(input);

    /// <summary>批量启用或停用选定部门。</summary>
    [HttpPost("batch-set-enabled")]
    public Task<BatchOperationResultDto> BatchSetEnabledAsync(BatchSetEnabledInput input)
        => _service.BatchSetEnabledAsync(input);
}
