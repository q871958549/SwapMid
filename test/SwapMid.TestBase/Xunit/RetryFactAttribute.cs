using System;
using Xunit;
using Xunit.Sdk;

namespace SwapMid.Xunit;

/// <summary>
/// 有界重试版 [Fact]:测试失败时自动重跑,至多 <see cref="MaxRetries"/> 次,全部失败才判红
/// (仅报告最后一轮的结果与输出)。照抄 xunit v2 官方示例仓 samples.xunit 的 RetryFactExample 模式。
///
/// ⚠️ 适用边界(防滥用):**仅**用于已锚定根因为「上游基础设施限制、本仓不可修」的偶发竞态场景
/// (先例:SqliteConnection 单连接与 .NET finalizer 线程的并发竞态,见
/// RecruitmentOnboardingTransactionTests 类注释与持久化记忆 efcore-tests-flaky-isolation),
/// 且必须满足:每轮重试之间无共享可变状态——xunit 每轮重建测试类实例,对 AbpIntegratedTest 派生类
/// 意味着全新 ABP Application + 全新 SQLite 连接,轮间天然隔离。
/// 它滤除的是「偶发假阴性」;真回归(被测逻辑确定性坏掉)每轮都失败,重试耗尽照样红,不会被掩盖。
/// 普通业务测试失败请修测试或修代码,禁止拿本特性当创可贴。
/// </summary>
[XunitTestCaseDiscoverer("SwapMid.Xunit.RetryFactDiscoverer", "SwapMid.TestBase")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class RetryFactAttribute : FactAttribute
{
    /// <summary>最大执行次数(含首轮);未设置或 &lt;1 时按 3 处理。</summary>
    public int MaxRetries { get; set; }
}
