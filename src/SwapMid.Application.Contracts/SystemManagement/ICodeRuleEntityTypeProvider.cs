using System.Collections.Generic;

namespace SwapMid.SystemManagement;

/// <summary>
/// 编码规则「可选业务对象」贡献者：**各业务域各实现一个**，注册自己拥有的 EntityType + 中文名 + 分组。
/// <see cref="ICodeRuleAppService.GetEntityTypesAsync"/> 经 <c>IEnumerable&lt;ICodeRuleEntityTypeProvider&gt;</c>
/// 注入聚合去重，供前端编码规则页的下拉选项与列表中文映射（单一事实源，前端不再硬编码闭集）。
///
/// ⚠️ 实现类**必须**标 <c>[ExposeServices(typeof(ICodeRuleEntityTypeProvider))]</c> + <c>ITransientDependency</c>：
/// ABP 默认只按「I{类名}」命名约定暴露接口，本接口名与实现类名不匹配 → 不显式 ExposeServices 则
/// IEnumerable 注入拿到**空集合**（编译绿、运行时静默丢失，坑见 [[abp-backend-pitfalls]]，须端到端测试兜底）。
/// </summary>
public interface ICodeRuleEntityTypeProvider
{
    /// <summary>本域贡献的可选业务对象清单。</summary>
    IEnumerable<CodeRuleEntityTypeDto> GetEntityTypes();
}
