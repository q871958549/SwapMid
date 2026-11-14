using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace SwapMid.Filtering;

public class ListQuerySortingExtensionsTests
{
    private static readonly IReadOnlyDictionary<
        string,
        Func<IQueryable<Row>, bool, IOrderedQueryable<Row>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<Row>, bool, IOrderedQueryable<Row>>>
        {
            ["name"] = (query, descending) => descending
                ? query.OrderByDescending(x => x.Name)
                : query.OrderBy(x => x.Name),
        };

    [Theory]
    [InlineData(",")]
    [InlineData(",,,")]
    [InlineData(" , , ")]
    public void ApplyWhitelistedSorting_Should_Fall_Back_For_Empty_Segments(
        string sorting)
    {
        var rows = new[]
        {
            new Row("B"),
            new Row("A"),
        }.AsQueryable();

        var result = rows
            .ApplyWhitelistedSorting(
                sorting,
                SortWhitelist,
                defaultKey: "name")
            .Select(x => x.Name)
            .ToArray();

        result.ShouldBe(new[] { "A", "B" });
    }

    [Fact]
    public void ApplyWhitelistedSorting_Should_Preserve_Default_Direction_For_Empty_Segments()
    {
        var rows = new[]
        {
            new Row("A"),
            new Row("B"),
        }.AsQueryable();

        var result = rows
            .ApplyWhitelistedSorting(
                ",,,",
                SortWhitelist,
                defaultKey: "name",
                defaultDescending: true)
            .Select(x => x.Name)
            .ToArray();

        result.ShouldBe(new[] { "B", "A" });
    }

    // ── Id 末级兜底键（2026-07-31）────────────────────────────────────────────────────────
    // 修的是正确性 bug：SQL 对并列行的相对顺序不作保证，而平台排序键大面积同值（批量导入/同步
    // 一次写数千行时间戳几乎全等），缺兜底时 OFFSET 分页会跨页重复/丢行。下列用例全部构造
    // **主排序键同值**的数据——没有兜底时结果顺序取决于源序，有兜底时才稳定按 Id。

    private static readonly IReadOnlyDictionary<
        string,
        Func<IQueryable<KeyedRow>, bool, IOrderedQueryable<KeyedRow>>> KeyedWhitelist =
        new Dictionary<string, Func<IQueryable<KeyedRow>, bool, IOrderedQueryable<KeyedRow>>>
        {
            ["name"] = (query, descending) => descending
                ? query.OrderByDescending(x => x.Name)
                : query.OrderBy(x => x.Name),
            ["id"] = (query, descending) => descending
                ? query.OrderByDescending(x => x.Id)
                : query.OrderBy(x => x.Id),
        };

    /// <summary>主键同值时按 Id 升序稳定——这正是缺兜底会跨页重复/丢行的场景。</summary>
    [Fact]
    public void ApplyWhitelistedSorting_Should_Append_Id_Tiebreaker_When_Sort_Key_Ties()
    {
        // 三行 Name 全同值、Id 故意逆序摆放：无兜底时输出即源序 3,1,2。
        var rows = new[] { new KeyedRow(3, "same"), new KeyedRow(1, "same"), new KeyedRow(2, "same") }
            .AsQueryable();

        var result = rows
            .ApplyWhitelistedSorting("name asc", KeyedWhitelist, defaultKey: "name")
            .Select(x => x.Id)
            .ToArray();

        result.ShouldBe(new[] { 1, 2, 3 });
    }

    /// <summary>方向跟随主序：降序请求配 Id 降序，这样 (主列, Id) 复合索引正反两向都能被索引扫描服务。</summary>
    [Fact]
    public void ApplyWhitelistedSorting_Should_Follow_Primary_Direction_For_Tiebreaker()
    {
        var rows = new[] { new KeyedRow(1, "same"), new KeyedRow(3, "same"), new KeyedRow(2, "same") }
            .AsQueryable();

        var result = rows
            .ApplyWhitelistedSorting("name desc", KeyedWhitelist, defaultKey: "name")
            .Select(x => x.Id)
            .ToArray();

        result.ShouldBe(new[] { 3, 2, 1 });
    }

    /// <summary>回退到默认键时同样追加兜底（未命中白名单的路径最容易被漏测）。</summary>
    [Fact]
    public void ApplyWhitelistedSorting_Should_Append_Tiebreaker_On_Fallback_Path()
    {
        var rows = new[] { new KeyedRow(2, "same"), new KeyedRow(1, "same") }.AsQueryable();

        var result = rows
            .ApplyWhitelistedSorting("不存在的列 asc", KeyedWhitelist, defaultKey: "name")
            .Select(x => x.Id)
            .ToArray();

        result.ShouldBe(new[] { 1, 2 });
    }

    /// <summary>
    /// opt-out：调用方在返回值上自带兜底键时必须能关掉，否则共享件追加的 Id 会排在调用方兜底键
    /// **之前**把它短路（外部同步表靠跨批次稳定的业务键兜底，被 Id 抢先就等于每次同步后翻页洗牌）。
    /// </summary>
    [Fact]
    public void ApplyWhitelistedSorting_Should_Respect_OptOut_So_Caller_Tiebreaker_Wins()
    {
        var rows = new[] { new KeyedRow(1, "same", "B"), new KeyedRow(2, "same", "A") }.AsQueryable();

        var result = rows
            .ApplyWhitelistedSorting("name asc", KeyedWhitelist, defaultKey: "name", appendIdTiebreaker: false)
            .ThenBy(x => x.Tag)
            .Select(x => x.Id)
            .ToArray();

        // 若共享件仍追加了 Id，结果会是 1,2（Id 抢在 Tag 之前）。
        result.ShouldBe(new[] { 2, 1 });
    }

    /// <summary>主序本身就是 Id 时不重复追加（少数白名单确有 id 键）。</summary>
    [Fact]
    public void ApplyWhitelistedSorting_Should_Not_Duplicate_When_Sorting_By_Id()
    {
        var rows = new[] { new KeyedRow(2, "b"), new KeyedRow(1, "a") }.AsQueryable();

        var sorted = rows.ApplyWhitelistedSorting("id desc", KeyedWhitelist, defaultKey: "name");

        sorted.Select(x => x.Id).ToArray().ShouldBe(new[] { 2, 1 });
        // 表达式树里只应有一个 OrderByDescending，没有多余的 ThenBy*。
        sorted.Expression.ToString().ShouldNotContain("ThenBy");
    }

    /// <summary>无 Id 属性的类型（join 投影行）静默跳过，不抛异常——投影类型不该被强加实体形状。</summary>
    [Fact]
    public void ApplyWhitelistedSorting_Should_Skip_Tiebreaker_For_Types_Without_Id()
    {
        var rows = new[] { new Row("B"), new Row("A") }.AsQueryable();

        var result = rows
            .ApplyWhitelistedSorting("name asc", SortWhitelist, defaultKey: "name")
            .Select(x => x.Name)
            .ToArray();

        result.ShouldBe(new[] { "A", "B" });
    }

    // ── 字符串版（转交动态 LINQ 的场景，唯一调用方是审计日志）────────────────────────────

    [Fact]
    public void ResolveWhitelistedSorting_Should_Append_Id_Tiebreaker()
    {
        var whitelist = new Dictionary<string, string> { ["time"] = "ExecutionTime" };

        ListQuerySortingExtensions
            .ResolveWhitelistedSorting("time desc", whitelist, "ExecutionTime DESC")
            .ShouldBe("ExecutionTime DESC, Id DESC");
    }

    [Fact]
    public void ResolveWhitelistedSorting_Should_Append_On_Fallback_And_Allow_OptOut()
    {
        var whitelist = new Dictionary<string, string> { ["time"] = "ExecutionTime" };

        // 未命中 → 回退串也要带兜底
        ListQuerySortingExtensions
            .ResolveWhitelistedSorting("不存在", whitelist, "ExecutionTime DESC")
            .ShouldBe("ExecutionTime DESC, Id DESC");
        // 显式关闭
        ListQuerySortingExtensions
            .ResolveWhitelistedSorting("time asc", whitelist, "ExecutionTime DESC", idFieldName: null)
            .ShouldBe("ExecutionTime ASC");
        // 回退串已含兜底字段时不重复追加
        ListQuerySortingExtensions
            .ResolveWhitelistedSorting(null, whitelist, "Id DESC")
            .ShouldBe("Id DESC");
    }

    private sealed record Row(string Name);

    private sealed record KeyedRow(int Id, string Name, string Tag = "");
}
