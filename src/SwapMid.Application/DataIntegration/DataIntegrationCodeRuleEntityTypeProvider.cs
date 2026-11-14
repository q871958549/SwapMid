using System.Collections.Generic;
using SwapMid.SystemManagement;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>数据集成域向编码规则注册可选业务对象（数据源/数据管道）。</summary>
[ExposeServices(typeof(ICodeRuleEntityTypeProvider))]
public class DataIntegrationCodeRuleEntityTypeProvider : ICodeRuleEntityTypeProvider, ITransientDependency
{
    public IEnumerable<CodeRuleEntityTypeDto> GetEntityTypes() => new[]
    {
        new CodeRuleEntityTypeDto { EntityType = "DataSource", DisplayName = "数据源", Group = "数据集成" },
        new CodeRuleEntityTypeDto { EntityType = "DataPipeline", DisplayName = "数据管道", Group = "数据集成" },
    };
}
