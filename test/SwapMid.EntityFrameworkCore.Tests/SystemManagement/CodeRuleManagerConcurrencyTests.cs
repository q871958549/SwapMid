using System;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Xunit;

namespace SwapMid.SystemManagement;

/// <summary>
/// <see cref="CodeRuleManager"/> 原子取号测试（R1 并发加固，蓝图 docs/project-management-blueprint.md §20.3）：
/// 并发取号无重复无跳漏、按重置周期（Daily/Monthly）正确归零、规则停用/不存在时抛 <c>CodeRuleNotFound</c>。
/// ⚠️ SQLite 全库写锁下的并发只验证「结果正确性」（无重复号、序号连续）——不代表 PostgreSQL 生产环境下的
/// 真实行锁并发吞吐/等待行为，PG 侧以生产冒烟为准（蓝图 §20.5 风险与验收注意）。
/// </summary>
[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
public class CodeRuleManagerConcurrencyTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly CodeRuleManager _codeRuleManager;
    private readonly IRepository<CodeRule, Guid> _codeRuleRepository;
    private readonly IGuidGenerator _guidGenerator;

    public CodeRuleManagerConcurrencyTests()
    {
        _codeRuleManager = GetRequiredService<CodeRuleManager>();
        _codeRuleRepository = GetRequiredService<IRepository<CodeRule, Guid>>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    private static string NewEntityType() => $"ConcurTest_{Guid.NewGuid():N}";

    /// <summary>直接插入一条规则（不经过种子），不设日期段、pad=1，便于从编码字符串直接反解序号数字。</summary>
    private async Task<string> SeedRuleAsync(string entityType, SequenceResetCycle resetCycle = SequenceResetCycle.Never,
        bool isEnabled = true, Action<CodeRule>? mutate = null)
    {
        var rule = new CodeRule(_guidGenerator.Create(), entityType, "并发测试规则", "CONCUR-",
            suffix: null, includeDate: false, dateFormat: null, sequencePad: 1, resetCycle: resetCycle,
            description: "R1 并发测试专用，即用即弃");
        if (!isEnabled)
        {
            rule.Disable();
        }
        mutate?.Invoke(rule);
        await WithUnitOfWorkAsync(() => _codeRuleRepository.InsertAsync(rule, autoSave: true));
        return entityType;
    }

    [Fact]
    public async Task GenerateAsync_并发取号_序号无重复无跳漏()
    {
        const int concurrency = 20;
        var entityType = await SeedRuleAsync(NewEntityType());

        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => _codeRuleManager.GenerateAsync(entityType))
            .ToArray();
        var codes = await Task.WhenAll(tasks);

        // 反解「CONCUR-」后的序号数字：应恰好是 1..concurrency 的一个排列（并发下顺序不定，但集合必须
        // 无重复、无跳漏——这正是 R1 要根治的核心正确性）。
        var sequences = codes.Select(c => int.Parse(c["CONCUR-".Length..])).OrderBy(x => x).ToArray();
        sequences.ShouldBe(Enumerable.Range(1, concurrency).ToArray());
    }

    [Fact]
    public async Task GenerateAsync_Daily周期_归零后连续两次取号_首次归零次次递增()
    {
        var entityType = NewEntityType();
        // 用一个明显早于「今天」的日期预先推进几次，模拟「上次取号在很久以前」——LastResetDate 落在
        // 该早期日期，CurrentSequence 已推进到 3；今天首次取号应归零重新从 1 开始（不是接着数到 4）。
        await SeedRuleAsync(entityType, SequenceResetCycle.Daily, mutate: rule =>
        {
            rule.NextSequence(new DateTime(2020, 1, 1));
            rule.NextSequence(new DateTime(2020, 1, 1));
            rule.NextSequence(new DateTime(2020, 1, 1));
        });

        var first = await _codeRuleManager.GenerateAsync(entityType);
        first.ShouldBe("CONCUR-1"); // 归零：今天与 2020-01-01 的 yyyyMMdd 键不同

        var second = await _codeRuleManager.GenerateAsync(entityType);
        second.ShouldBe("CONCUR-2"); // 同一天内第二次：不再归零，正常递增
    }

    [Fact]
    public async Task GenerateAsync_Monthly周期_归零后连续两次取号_首次归零次次递增()
    {
        var entityType = NewEntityType();
        await SeedRuleAsync(entityType, SequenceResetCycle.Monthly, mutate: rule =>
        {
            rule.NextSequence(new DateTime(2020, 1, 15));
            rule.NextSequence(new DateTime(2020, 1, 15));
        });

        var first = await _codeRuleManager.GenerateAsync(entityType);
        first.ShouldBe("CONCUR-1"); // 归零：本月与 2020-01 的 yyyyMM 键不同

        var second = await _codeRuleManager.GenerateAsync(entityType);
        second.ShouldBe("CONCUR-2");
    }

    [Fact]
    public async Task GenerateAsync_规则已停用_应抛CodeRuleNotFound()
    {
        var entityType = await SeedRuleAsync(NewEntityType(), isEnabled: false);

        var ex = await Should.ThrowAsync<BusinessException>(() => _codeRuleManager.GenerateAsync(entityType));

        ex.Code.ShouldBe(SwapMidDomainErrorCodes.SystemManagement.CodeRuleNotFound);
    }

    [Fact]
    public async Task GenerateAsync_规则不存在_应抛CodeRuleNotFound()
    {
        var ex = await Should.ThrowAsync<BusinessException>(() => _codeRuleManager.GenerateAsync(NewEntityType()));

        ex.Code.ShouldBe(SwapMidDomainErrorCodes.SystemManagement.CodeRuleNotFound);
    }
}
