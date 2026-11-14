using System.Collections.Generic;
using SwapMid.SystemManagement;
using Volo.Abp.DependencyInjection;

namespace SwapMid.MessageBus;

/// <summary>消息总线域向编码规则注册可选业务对象（消息主题/订阅）。</summary>
[ExposeServices(typeof(ICodeRuleEntityTypeProvider))]
public class MessageBusCodeRuleEntityTypeProvider : ICodeRuleEntityTypeProvider, ITransientDependency
{
    public IEnumerable<CodeRuleEntityTypeDto> GetEntityTypes() => new[]
    {
        new CodeRuleEntityTypeDto { EntityType = "MessageTopic", DisplayName = "消息主题", Group = "消息总线" },
        new CodeRuleEntityTypeDto { EntityType = "MessageSubscription", DisplayName = "消息订阅", Group = "消息总线" },
    };
}
