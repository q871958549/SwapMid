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

    [HttpGet]
    public Task<PagedResultDto<CodeRuleDto>> GetListAsync(GetCodeRuleListInput input)
        => _service.GetListAsync(input);

    [HttpGet("{id:guid}")]
    public Task<CodeRuleDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    [HttpPost]
    public Task<CodeRuleDto> CreateAsync(CodeRuleCreateUpdateDto input)
        => _service.CreateAsync(input);

    [HttpPut("{id:guid}")]
    public Task<CodeRuleDto> UpdateAsync(Guid id, CodeRuleCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    [HttpDelete("{id:guid}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    [HttpPost("generate")]
    public Task<string> GenerateAsync([FromQuery] string entityType)
        => _service.GenerateAsync(entityType);

    /// <summary>可选业务对象清单（各域注册；前端下拉选项与列表中文名映射的单一事实源）。</summary>
    [HttpGet("entity-types")]
    public Task<List<CodeRuleEntityTypeDto>> GetEntityTypesAsync()
        => _service.GetEntityTypesAsync();
}
