using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SwapMid.SystemManagement;

public interface ICodeRuleAppService : IApplicationService
{
    Task<PagedResultDto<CodeRuleDto>> GetListAsync(GetCodeRuleListInput input);
    Task<CodeRuleDto> GetAsync(Guid id);
    Task<CodeRuleDto> CreateAsync(CodeRuleCreateUpdateDto input);
    Task<CodeRuleDto> UpdateAsync(Guid id, CodeRuleCreateUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<string> GenerateAsync(string entityType);

    /// <summary>可选业务对象清单（各域注册，去重按分组排序）；前端下拉选项与列表中文映射的单一事实源。</summary>
    Task<List<CodeRuleEntityTypeDto>> GetEntityTypesAsync();
}
