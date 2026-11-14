using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Volo.Abp.Testing;
using Xunit;

namespace SwapMid.EntityFrameworkCore;

/// <summary>
/// 并行调度约定的架构守卫:本程序集内,凡构建 ABP Application 的测试类(继承
/// <c>AbpIntegratedTest&lt;TModule&gt;</c>,意味着持有 SQLite 内存连接、容器构建、数据种子、
/// GC 终结器排空等重量级操作)必须**直接**声明 [Collection(...)],且指向**本程序集内已定义的**
/// 某个 CollectionDefinition——不得游离成与其它集合并行调度的 xunit 隐式集合。
///
/// 为什么必须(2026-07-17 实证,三阶段修复的教训,完整记录见 RecruitmentOnboardingTransactionTests
/// 类 remarks):xunit v2 以「测试集合」为并行调度单位,未挂 [Collection] 的类各自成隐式集合、与
/// 所有其它集合并行执行。持真实 UoW 事务的测试类曾因此偶发失败;而「哪些类没挂也无害」靠人眼推断
/// 已被两轮修复实证不可靠(第一轮只挂了失败类自己,漏了同样游离、每个 [Fact] 都构建完整 Application
/// 并做 GC 终结器排空的 MessageDeliveryClientCollectionTests——它与活动真事务并行时显著放大
/// SqliteConnection 单连接竞态)。故收敛为本守卫:构建 Application 就必须进显式受控集合,无一例外。
/// 纯内存测试类(不继承 AbpIntegratedTest,如 RecruitmentOfferTokenExpiryTests)不碰共享进程资源,
/// 留在并行侧无害,不在断言范围。
///
/// 📌 2026-07-29 变更(CI 提速):受控集合从**单个**扩为**按业务域的一组**
/// (<see cref="EntityFrameworkCoreTestCollections"/>)。此前 100 个类同挂一个集合 = 完全串行,
/// CI 实测 825 个测试 16m56s 独占关键路径。拆分后集合之间并行、集合之内仍串行。
/// 守卫语义未放松:白名单只是从"那一个集合"变成"本程序集显式定义的任一集合",
/// 游离仍然是错误。放行并行的正确性前提(逐条已核实):
///   ①每个 [Fact] 新建独立容器 + 独立 :memory: SQLite 连接,测试之间不共享连接;
///   ②历史两个 flaky 根因(SqliteConnection 跨线程竞态、SettingManagement 后台事务)的修复方式
///     是**消除竞态源**(GC 排空 / Options 双开关),不是"靠串行躲开";
///   ③唯一残留的进程级可变状态(FakeMessageDelivery* 的 static 集合)已改为容器单例
///     <c>MessageDeliveryRecorder</c>——否则任何触发投递的测试都会污染并发中的断言方。
/// 若未来新增测试引入了新的进程级共享状态,应当把它改成容器作用域,而不是把集合合并回去。
///
/// 判据说明:xunit v2 读取 [Collection] 只认测试类**自身直接声明**(ReflectionTypeInfo 不沿继承链
/// 查找),故本守卫同样只查直接声明——写在基类上是不生效的,别这么绕。
/// 本守卫自身纯反射、不构建 Application,依上述规则天然豁免。
/// </summary>
public class TestParallelismConventionTests
{
    [Fact]
    public void 凡构建ABP应用的测试类必须直接挂入受控Collection()
    {
        var assembly = typeof(TestParallelismConventionTests).Assembly;

        // 受控集合 = 本程序集内显式声明的全部 CollectionDefinition(含按业务域拆分的并行集合,
        // 以及 DisableParallelization = true 的专属串行集合)。取并集而非硬编码名单,
        // 新增集合时无需同步改本守卫,但"必须显式挂一个"这条约束不变。
        var allowedNames = assembly.GetTypes()
            .SelectMany(t => t.CustomAttributes)
            .Where(a => a.AttributeType == typeof(CollectionDefinitionAttribute)
                && a.ConstructorArguments.Count == 1)
            .Select(a => a.ConstructorArguments[0].Value as string)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet();

        allowedNames.ShouldNotBeEmpty("本程序集应至少定义一个 CollectionDefinition,否则守卫失去意义");

        var offenders = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(InheritsAbpIntegratedTest)
            .Where(t => !allowedNames.Contains(GetDeclaredCollectionName(t)))
            .Select(t => t.FullName)
            .OrderBy(n => n)
            .ToList();

        offenders.ShouldBeEmpty(
            "以下测试类构建 ABP Application 却未直接挂入本程序集定义的受控 Collection,会以独立隐式集合" +
            "与其它集合并行执行,与 SQLite 连接/真事务产生进程级竞态放大(见本类注释);" +
            $"请按业务域从 {nameof(EntityFrameworkCoreTestCollections)} 选一个挂上:{string.Join("、", offenders)}");
    }

    [Fact]
    public void 凡构建ABP应用的测试类必须带Slow分类供PR过滤()
    {
        var assembly = typeof(TestParallelismConventionTests).Assembly;
        var offenders = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(InheritsAbpIntegratedTest)
            .Where(t => !HasSlowCategory(t))
            .Select(t => t.FullName)
            .OrderBy(n => n)
            .ToList();

        offenders.ShouldBeEmpty(
            "以下 ABP 集成测试未标 Category=Slow，会被 PR 分层门禁误跑、拉长关键路径。" +
            $"请在类或 {nameof(SwapMidEntityFrameworkCoreTestBase)} 上加 [Trait(\"{SlowTest.CategoryName}\", \"{SlowTest.CategoryValue}\")]：" +
            string.Join("、", offenders));
    }

    private static bool InheritsAbpIntegratedTest(Type type)
    {
        for (var t = type.BaseType; t != null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(AbpIntegratedTest<>))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>取类自身直接声明的 [Collection] 名(CustomAttributes 不含继承,与 xunit v2 语义一致);未声明返回 null。</summary>
    private static string? GetDeclaredCollectionName(Type type)
    {
        var attr = type.CustomAttributes.FirstOrDefault(a =>
            a.AttributeType == typeof(CollectionAttribute) && a.ConstructorArguments.Count == 1);
        return attr?.ConstructorArguments[0].Value as string;
    }

    private static bool HasSlowCategory(Type type)
    {
        // TraitAttribute 的 Name/Value 不是公开属性；与上方 Collection 守卫一样读构造参数。
        // CustomAttributes 不含继承，必须沿基类走，才能认到打在 EntityFrameworkCoreTestBase 上的标记。
        for (var t = type; t != null; t = t.BaseType)
        {
            if (t.CustomAttributes.Any(IsSlowTrait))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSlowTrait(System.Reflection.CustomAttributeData attribute)
    {
        return attribute.AttributeType == typeof(TraitAttribute)
            && attribute.ConstructorArguments.Count == 2
            && attribute.ConstructorArguments[0].Value as string == SlowTest.CategoryName
            && attribute.ConstructorArguments[1].Value as string == SlowTest.CategoryValue;
    }
}
