using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace SwapMid.SystemManagement;

/// <summary>
/// 编码规则「可选业务对象」聚合端到端测试。核心是兜底 ABP「IEnumerable 收集 + ExposeServices」陷阱：
/// 任一 provider 漏标 <c>[ExposeServices(typeof(ICodeRuleEntityTypeProvider))]</c> 会被 DI 静默丢弃、
/// 集合缺项（编译绿、运行时才空），只有走真实容器解析才逼得出来。
/// </summary>
public abstract class CodeRuleEntityTypeProviderTests<TStartupModule> : SwapMidApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ICodeRuleAppService _codeRuleAppService;

    protected CodeRuleEntityTypeProviderTests()
    {
        _codeRuleAppService = GetRequiredService<ICodeRuleAppService>();
    }

    [Fact]
    public async Task GetEntityTypesAsync_应聚合各域贡献者_非空且含多域代表_每条有中文名与分组()
    {
        var list = await _codeRuleAppService.GetEntityTypesAsync();

        // 空/缺项 = 某域 provider 漏标 [ExposeServices]（IEnumerable 注入静默丢失）。
        list.ShouldNotBeEmpty();
        var types = list.Select(x => x.EntityType).ToHashSet();
        types.ShouldContain("Enterprise");         // 企业管理域
        types.ShouldContain("ApiGroup");           // API 管理域
        types.ShouldContain("ApiRoute");           // API 管理域
        types.ShouldContain("DataSource");         // 数据集成域
        types.ShouldContain("DataPipeline");       // 数据集成域
        types.ShouldContain("MessageTopic");       // 消息总线域

        // 每条都有中文显示名与分组（前端下拉/列表映射依赖）。
        list.ShouldAllBe(x => !string.IsNullOrWhiteSpace(x.DisplayName) && !string.IsNullOrWhiteSpace(x.Group));
        // EntityType 唯一（跨域去重）。
        types.Count.ShouldBe(list.Count);
    }
}
