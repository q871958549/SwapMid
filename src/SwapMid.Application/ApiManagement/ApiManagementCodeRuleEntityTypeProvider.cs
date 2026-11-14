using System.Collections.Generic;
using SwapMid.SystemManagement;
using Volo.Abp.DependencyInjection;

namespace SwapMid.ApiManagement;

/// <summary>API 管理域向编码规则注册可选业务对象（API 分组/路由）。</summary>
[ExposeServices(typeof(ICodeRuleEntityTypeProvider))]
public class ApiManagementCodeRuleEntityTypeProvider : ICodeRuleEntityTypeProvider, ITransientDependency
{
    public IEnumerable<CodeRuleEntityTypeDto> GetEntityTypes() => new[]
    {
        new CodeRuleEntityTypeDto { EntityType = "ApiGroup", DisplayName = "API分组", Group = "API管理" },
        new CodeRuleEntityTypeDto { EntityType = "ApiRoute", DisplayName = "API路由", Group = "API管理" },
    };
}
