using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwapMid.SystemManagement;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;

namespace SwapMid.Controllers.SystemManagement;

[RemoteService]
[Area("app")]
[Route("api/v1/code-rules")]
public class CodeRuleController : SwapMidController, ICodeRuleAppService
{
    private readonly ICodeRuleAppService _service;

    public CodeRuleController(ICodeRuleAppService service)
    {
        _service = service;
    }

    /// <summary>分页查询业务单据自动编码规则列表。</summary>
    [HttpGet]
    public Task<PagedResultDto<CodeRuleDto>> GetListAsync(GetCodeRuleListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单条编码规则配置详情。</summary>
    [HttpGet("{id:guid}")]
    public Task<CodeRuleDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    /// <summary>创建业务对象编码规则。</summary>
    [HttpPost]
    public Task<CodeRuleDto> CreateAsync(CodeRuleCreateUpdateDto input)
        => _service.CreateAsync(input);

    /// <summary>更新编码规则前缀、日期格式与流水号步长配置。</summary>
    [HttpPut("{id:guid}")]
    public Task<CodeRuleDto> UpdateAsync(Guid id, CodeRuleCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>删除指定编码规则。</summary>
    [HttpDelete("{id:guid}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    /// <summary>按指定业务对象类型即时原子生成下一个唯一业务编号。</summary>
    [HttpPost("generate")]
    public Task<string> GenerateAsync([FromQuery] string entityType)
        => _service.GenerateAsync(entityType);

    /// <summary>可选业务对象清单（各域注册；前端下拉选项与列表中文名映射的单一事实源）。</summary>
    [HttpGet("entity-types")]
    public Task<List<CodeRuleEntityTypeDto>> GetEntityTypesAsync()
        => _service.GetEntityTypesAsync();
}
