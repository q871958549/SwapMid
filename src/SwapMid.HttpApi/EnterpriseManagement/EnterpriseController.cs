using System;
using System.Threading.Tasks;
using SwapMid.EnterpriseManagement;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace SwapMid.Controllers.EnterpriseManagement;

/// <summary>企业管理 — 企业主体信息的增删改查及主体企业设置</summary>
[RemoteService]
[Area("app")]
[Route("api/v1/enterprises")]
public class EnterpriseController : SwapMidController, IEnterpriseAppService
{
    private readonly IEnterpriseAppService _service;

    public EnterpriseController(IEnterpriseAppService service)
    {
        _service = service;
    }

    /// <summary>获取企业列表（分页，支持编码/名称过滤）</summary>
    [HttpGet]
    public Task<PagedResultDto<EnterpriseDto>> GetListAsync(GetEnterpriseListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单个企业详情</summary>
    [HttpGet("{id}")]
    public Task<EnterpriseDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    /// <summary>新建企业</summary>
    [HttpPost]
    public Task<EnterpriseDto> CreateAsync(EnterpriseCreateUpdateDto input)
        => _service.CreateAsync(input);

    /// <summary>更新企业信息</summary>
    [HttpPut("{id}")]
    public Task<EnterpriseDto> UpdateAsync(Guid id, EnterpriseCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>删除企业（级联删除部门/岗位/员工）</summary>
    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    /// <summary>将指定企业设为主体企业（同一租户仅允许一个主体）</summary>
    [HttpPut("{id}/set-as-main")]
    public Task SetAsMainAsync(Guid id)
        => _service.SetAsMainAsync(id);

    /// <summary>获取当前租户的主体企业</summary>
    [HttpGet("main")]
    public Task<EnterpriseDto?> GetMainAsync()
        => _service.GetMainAsync();
}
