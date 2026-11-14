using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace SwapMid.Scheduling;

/// <summary>
/// <see cref="ScheduledJobHandlerResolver"/> 的故障隔离契约。
///
/// 背景：此前 <c>ScheduledJobRunner</c> 与管控页都直接注入 <c>IEnumerable&lt;IScheduledJobHandler&gt;</c>，
/// 解析该集合会一次性构造全部处理器 —— 任一处理器依赖链断裂就让**全部**定时任务在作业激活期失败，
/// 且失败落在 Runner 的 try 之外，执行历史零痕迹。本组测试锁定「坏一个只坏一个」这条不变量。
/// </summary>
[Collection(EntityFrameworkCoreTestCollections.Scheduling)]
public sealed class ScheduledJobHandlerResolverTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly ScheduledJobHandlerCatalog _catalog;
    private readonly IScheduledJobHandlerResolver _resolver;

    public ScheduledJobHandlerResolverTests()
    {
        _catalog = GetRequiredService<ScheduledJobHandlerCatalog>();
        _resolver = GetRequiredService<IScheduledJobHandlerResolver>();
    }

    // 清单抓取时机正确性：ABP 约定注册若晚于模块 ConfigureServices，这里会是空/残缺，
    // 生产表现为「全部定时任务找不到 handler」。断言它与容器里真实注册的集合逐一对齐。
    [Fact]
    public void 处理器清单_与容器注册的集合完全一致()
    {
        var registered = GetRequiredService<IEnumerable<IScheduledJobHandler>>()
            .Select(handler => handler.GetType())
            .ToList();

        _catalog.HandlerTypes.Count.ShouldBe(registered.Count);
        _catalog.HandlerTypes.ShouldBe(registered, ignoreOrder: true);
    }

    // 全量构造冒烟：等价于「生产上作业激活时不会因某个处理器坏死而炸」。
    // 相比原先的集合注入，这里失败时能明确指出是哪个处理器 —— 集合注入只会整体抛一个 DI 异常。
    [Fact]
    public void 全部已注册处理器_均可成功构造()
    {
        var available = _resolver.ResolveAvailable();

        var failed = _catalog.HandlerTypes
            .Except(available.Select(handler => handler.GetType()))
            .Select(type => type.Name)
            .ToList();

        failed.ShouldBeEmpty($"以下处理器无法构造，其对应定时任务将无法执行：{string.Join("、", failed)}");
        available.Count.ShouldBe(_catalog.HandlerTypes.Count);
    }

    // JobKey 撞车会让「JobKey → 类型」索引后者覆盖前者，表现为某任务静默执行成了另一个任务。
    [Fact]
    public void 处理器的JobKey_互不重复()
    {
        var duplicated = _resolver.ResolveAvailable()
            .GroupBy(handler => handler.JobKey)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        duplicated.ShouldBeEmpty($"JobKey 重复：{string.Join("、", duplicated)}");
    }

    [Fact]
    public void Find_能按JobKey定位到对应处理器()
    {
        var handler = _resolver.Find(ScheduledJobKeys.AuditLogCleanup);

        handler.ShouldNotBeNull();
        handler!.ShouldBeOfType<AuditLogCleanupHandler>();
        handler.JobKey.ShouldBe(ScheduledJobKeys.AuditLogCleanup);
    }

    [Fact]
    public void Find_未知JobKey_返回null而不抛异常()
    {
        _resolver.Find("不存在的任务键").ShouldBeNull();
    }

    // 核心不变量：清单里混入一个构造必然失败的处理器（此处用未注册进容器的类型模拟依赖链断裂），
    // 其余任务照常解析 —— 这正是「一个处理器坏死不再连坐全部定时任务」。
    [Fact]
    public void 单个处理器构造失败_不影响其余处理器解析()
    {
        var resolver = CreateResolverOver(typeof(构造必失败的处理器), typeof(AuditLogCleanupHandler));

        var handler = resolver.Find(ScheduledJobKeys.AuditLogCleanup);

        handler.ShouldNotBeNull("坏处理器排在清单前面时，也不得妨碍后面的处理器被解析");
        handler!.ShouldBeOfType<AuditLogCleanupHandler>();
    }

    [Fact]
    public void ResolveAvailable_跳过构造失败的处理器_保留其余()
    {
        var resolver = CreateResolverOver(typeof(构造必失败的处理器), typeof(AuditLogCleanupHandler));

        var available = resolver.ResolveAvailable();

        available.Count.ShouldBe(1);
        available[0].ShouldBeOfType<AuditLogCleanupHandler>();
    }

    // 目标处理器自己坏死时返回 null（而非抛异常穿出）：调用方据此走「未找到 handler」的正常失败路径，
    // 于是失败仍能被记进执行历史 + 告警，不会绕过 Runner 的保护圈。
    [Fact]
    public void Find_目标处理器构造失败_返回null而不抛异常()
    {
        var resolver = CreateResolverOver(typeof(构造必失败的处理器));

        Should.NotThrow(() => resolver.Find("构造必失败的处理器对应的键").ShouldBeNull());
    }

    [Fact]
    public void 处理器清单为空_直接拒绝启动()
    {
        // 空清单意味着全部定时任务静默失效，比启动失败难查得多，故必须 fail-fast。
        Should.Throw<InvalidOperationException>(() => new ScheduledJobHandlerCatalog(Array.Empty<Type>()));
    }

    private ScheduledJobHandlerResolver CreateResolverOver(params Type[] handlerTypes)
        => new(
            new ScheduledJobHandlerCatalog(handlerTypes),
            GetRequiredService<IServiceProvider>(),
            NullLogger<ScheduledJobHandlerResolver>.Instance);

    /// <summary>模拟「依赖链断裂」的处理器：故意不注册进容器，解析时必抛。
    /// 用未注册代替「构造函数里抛异常」，是因为生产上这类故障几乎都表现为依赖解析不出来。</summary>
    private sealed class 构造必失败的处理器 : IScheduledJobHandler
    {
        public string JobKey => "构造必失败的处理器对应的键";

        public Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("本处理器只用于验证解析失败路径，不应被执行");
    }
}
