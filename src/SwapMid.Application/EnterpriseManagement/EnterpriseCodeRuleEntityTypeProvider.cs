using System.Collections.Generic;
using SwapMid.SystemManagement;
using Volo.Abp.DependencyInjection;

namespace SwapMid.EnterpriseManagement;

/// <summary>企业管理域向编码规则注册可选业务对象（企业/部门/员工/岗位）。</summary>
[ExposeServices(typeof(ICodeRuleEntityTypeProvider))]
public class EnterpriseCodeRuleEntityTypeProvider : ICodeRuleEntityTypeProvider, ITransientDependency
{
    public IEnumerable<CodeRuleEntityTypeDto> GetEntityTypes() => new[]
    {
        new CodeRuleEntityTypeDto { EntityType = "Enterprise", DisplayName = "企业", Group = "企业管理" },
        new CodeRuleEntityTypeDto { EntityType = "Department", DisplayName = "部门", Group = "企业管理" },
        new CodeRuleEntityTypeDto { EntityType = "Employee", DisplayName = "员工", Group = "企业管理" },
        new CodeRuleEntityTypeDto { EntityType = "Position", DisplayName = "岗位", Group = "企业管理" },
    };
}
