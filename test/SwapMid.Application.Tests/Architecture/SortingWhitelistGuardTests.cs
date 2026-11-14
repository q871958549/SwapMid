using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

/// <summary>
/// **排序白名单门禁**——把 <c>src/CLAUDE.md</c> 的排序铁律从文字约定变成可执行断言。
///
/// 用户可控的排序串（<c>input.Sorting</c>）只有三个合法出口，各自对应一类下游：
/// <list type="bullet">
/// <item>EF <c>IQueryable</c> → <c>ListQuerySortingExtensions.ApplyWhitelistedSorting</c></item>
/// <item>转交第三方 API / 动态 LINQ（如 ABP <c>IAuditLogRepository</c>）→ <c>ResolveWhitelistedSorting</c></item>
/// <item>裸 SQL → <c>ScanQueryAppServiceBase.BuildOrderByClause</c></item>
/// </list>
///
/// 绕开它们有两种后果，性质完全不同：
/// <list type="number">
/// <item><b>可用性</b>：非法字段名裸传动态 LINQ 会抛异常返 500——前端点一下没配好的列头就白屏。</item>
/// <item><b>安全</b>：拼进 SQL 就是排序注入面。本门禁对这一类**不提供基线豁免**（见下）。</item>
/// </list>
///
/// ## 为什么门禁盯的是「Sorting 的去向」而不是「有没有调用白名单」
/// 后者只能证明某处调用过共享件，证明不了**这个** sorting 值去了哪。盯去向才能覆盖到
/// 「转发给仓储方法」这类间接路径：AppService 把 <c>input.Sorting</c> 透传给仓储，安全性取决于
/// 仓储实现——那些透传点登记进基线，基线本身就是「这些点的下游需要人工确认」的台账。
/// （2026-08-03 核实：5 个透传点的仓储实现全部落在 <c>ApplyWhitelistedSorting</c>，
/// 唯一例外 <c>EfCoreKnowledgeArticleRepository</c> 是显式固定排序、明确拒收 sorting 串。）
/// </summary>
public class SortingWhitelistGuardTests
{
    /// <summary>三个合法出口（见类注释）。</summary>
    private static readonly HashSet<string> WhitelistResolvers = new(StringComparer.Ordinal)
    {
        "ApplyWhitelistedSorting",
        "ResolveWhitelistedSorting",
        "BuildOrderByClause",
    };

    private const string BaselineFileName =
        "test/SwapMid.Application.Tests/Architecture/sorting-passthrough-baseline.txt";

    [Fact]
    public void Sorting串不得绕开白名单解析器()
    {
        var baseline = LoadBaseline();
        var offenders = Scan()
            .Where(hit => !hit.IsStringComposition)
            .Where(hit => !baseline.Contains(hit.Key))
            .ToList();

        offenders.ShouldBeEmpty(
            "以下位置读取了用户可控的 Sorting 串，却没有直接交给白名单解析器\n" +
            $"（{string.Join(" / ", WhitelistResolvers)}）。非法字段名裸传动态 LINQ 会抛异常返 500。\n" +
            "若这里是**透传给下游方法**、由下游过白名单，把 key 行追加进 " + BaselineFileName + "，\n" +
            "并在下游确认它真的走了白名单——基线是「待确认」台账，不是许可证：\n" +
            string.Join("\n", offenders.Select(hit => $"  {hit.Key}\n      ({hit.Location}) {hit.Snippet}")));
    }

    /// <summary>
    /// 拼字符串是排序注入面，**没有基线豁免**——不像透传那样存在"下游可能是安全的"这种可能性，
    /// 把用户串拼进 SQL/LINQ 文本本身就是缺陷。真需要动态排序请走
    /// <c>ResolveWhitelistedSorting</c>（它返回的是白名单映射后的安全串）。
    /// </summary>
    [Fact]
    public void Sorting串不得参与字符串拼接或插值()
    {
        var offenders = Scan().Where(hit => hit.IsStringComposition).ToList();

        offenders.ShouldBeEmpty(
            "以下位置把用户可控的 Sorting 串拼进了字符串——这是排序注入面，本门禁不提供豁免。\n" +
            "请改用 ResolveWhitelistedSorting（转交动态 LINQ）或 BuildOrderByClause（裸 SQL），\n" +
            "它们返回的是白名单映射后的安全串：\n" +
            string.Join("\n", offenders.Select(hit => $"  ({hit.Location}) {hit.Snippet}")));
    }

    [Fact]
    public void 基线条目应真实存在_否则请删除豁免()
    {
        var live = Scan().Select(hit => hit.Key).ToHashSet(StringComparer.Ordinal);

        var stale = LoadBaseline().Where(entry => !live.Contains(entry)).ToList();

        stale.ShouldBeEmpty(
            $"以下基线条目已不存在。基线只能随整改缩短，请从 {BaselineFileName} 删除：\n" +
            string.Join("\n", stale.Select(entry => $"  {entry}")));
    }

    [Fact]
    public void CrudAppService子类不得落入ABP基类动态LINQ排序()
    {
        var appAssembly = typeof(SwapMidApplicationModule).Assembly;
        var bindingFlags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;

        var offenders = new List<string>();
        foreach (var type in appAssembly.GetTypes())
        {
            if (type.IsAbstract || !type.IsClass)
            {
                continue;
            }

            if (!InheritsAbpReadOnlyAppService(type))
            {
                continue;
            }

            var getList = type.GetMethods(bindingFlags).FirstOrDefault(m => m.Name == "GetListAsync" && m.GetParameters().Length == 1);
            var applySorting = type.GetMethods(bindingFlags).FirstOrDefault(m => m.Name == "ApplySorting" && m.GetParameters().Length == 2);

            var overridesGetList = getList?.DeclaringType?.Namespace?.StartsWith("Volo.Abp", StringComparison.Ordinal) == false;
            var overridesApplySorting = applySorting?.DeclaringType?.Namespace?.StartsWith("Volo.Abp", StringComparison.Ordinal) == false;

            if (!overridesGetList && !overridesApplySorting)
            {
                offenders.Add(type.FullName ?? type.Name);
            }
        }

        offenders.ShouldBeEmpty(
            "以下 CrudAppService/ReadOnlyAppService 子类既未重写 GetListAsync，也未重写 ApplySorting，\n" +
            "会导致用户可控的 input.Sorting 落入 ABP 基类 System.Linq.Dynamic.Core 动态 LINQ 排序（非法列名直接抛 500）。\n" +
            "请重写 ApplySorting 或 GetListAsync 并接入 ApplyWhitelistedSorting：\n  " +
            string.Join("\n  ", offenders));

        var srcDir = LocateSrcDirectory();
        srcDir.ShouldNotBeNull("未能定位 src 目录");
        var baseApplySortingHits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir!, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcDir!, file).Replace('\\', '/');
            if (IsGenerated(rel))
            {
                continue;
            }

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call.Expression is MemberAccessExpressionSyntax
                    {
                        Expression: BaseExpressionSyntax,
                        Name.Identifier.ValueText: "ApplySorting",
                    })
                {
                    var line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    baseApplySortingHits.Add($"{rel}:{line}");
                }
            }
        }

        baseApplySortingHits.ShouldBeEmpty(
            "禁止在 ApplySorting 重写中调用 base.ApplySorting(query, input) 回退到 ABP 动态 LINQ 排序，请改用 ApplyWhitelistedSorting：\n  " +
            string.Join("\n  ", baseApplySortingHits));
    }

    private static bool InheritsAbpReadOnlyAppService(Type type)
    {
        for (var cur = type.BaseType; cur != null; cur = cur.BaseType)
        {
            if (cur.IsGenericType &&
                cur.Namespace == "Volo.Abp.Application.Services" &&
                cur.Name.StartsWith("AbstractKeyReadOnlyAppService`", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // ── 扫描器自检 ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("var q = src.ApplyWhitelistedSorting(input.Sorting, Whitelist, \"name\");")]
    [InlineData("var s = ListQuerySortingExtensions.ResolveWhitelistedSorting(input.Sorting, Whitelist, \"Id DESC\");")]
    [InlineData("var o = BuildOrderByClause(input.Sorting, Whitelist, \"create_time DESC\");")]
    // 空合并等表达式包裹后仍应识别为「进了解析器」。
    [InlineData("var q = src.ApplyWhitelistedSorting(input.Sorting ?? \"name\", Whitelist, \"name\");")]
    // 写入不是危险读取。
    [InlineData("input.Sorting = null;")]
    // nameof 不是真读取。
    [InlineData("var n = nameof(input.Sorting);")]
    public void 扫描器不应误报合法用法(string body)
    {
        ScanSnippet(body).ShouldBeEmpty();
    }

    [Theory]
    // 裸传动态 LINQ：本门禁存在的首要理由。
    [InlineData("var q = src.OrderBy(input.Sorting);", false)]
    // 透传下游：报出来、可进基线。
    [InlineData("var r = await _repo.GetListAsync(input.Sorting, 10, 0);", false)]
    // DTO 转发。
    [InlineData("var dto = new Query { Sorting = input.Sorting };", false)]
    // 字符串插值 / 拼接：注入面，无豁免。
    [InlineData("var sql = $\"ORDER BY {input.Sorting}\";", true)]
    [InlineData("var sql = \"ORDER BY \" + input.Sorting;", true)]
    public void 扫描器应识别绕开白名单的用法(string body, bool expectStringComposition)
    {
        var hits = ScanSnippet(body);

        hits.Count.ShouldBe(1);
        hits[0].IsStringComposition.ShouldBe(expectStringComposition);
    }

    private static IReadOnlyList<Hit> ScanSnippet(string body)
    {
        var source = $$"""
            public class Probe
            {
                public async Task M()
                {
            {{body}}
                }
            }
            """;

        return ScanRoot(CSharpSyntaxTree.ParseText(source).GetRoot(), "probe.cs").ToList();
    }

    // ── 扫描实现 ────────────────────────────────────────────────────────────────────────

    private readonly record struct Hit(
        string Key,
        string Location,
        string Snippet,
        bool IsStringComposition);

    private static List<Hit> Scan()
    {
        var srcDir = LocateSrcDirectory();
        srcDir.ShouldNotBeNull("未能定位 src 目录");

        var hits = new List<Hit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(srcDir!, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcDir!, file).Replace('\\', '/');
            if (IsGenerated(rel))
            {
                continue;
            }

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var hit in ScanRoot(root, rel))
            {
                if (seen.Add(hit.Key))
                {
                    hits.Add(hit);
                }
            }
        }

        return hits;
    }

    private static IEnumerable<Hit> ScanRoot(SyntaxNode root, string relPath)
    {
        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (access.Name.Identifier.ValueText != "Sorting")
            {
                continue;
            }

            if (IsWriteTarget(access) || IsInsideNameOf(access))
            {
                continue;
            }

            if (FlowsIntoResolver(access))
            {
                continue;
            }

            var owner = DescribeOwner(access);
            var composition = IsStringComposition(access);
            var line = access.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var statement = access.FirstAncestorOrSelf<StatementSyntax>();

            yield return new Hit(
                $"{relPath}::{owner}",
                $"{relPath}:{line}",
                Compact((statement ?? (SyntaxNode)access).ToString()),
                composition);
        }
    }

    /// <summary>赋值左侧（<c>input.Sorting = x</c>）是写入，不是危险读取。</summary>
    private static bool IsWriteTarget(MemberAccessExpressionSyntax access) =>
        access.Parent is AssignmentExpressionSyntax assignment && assignment.Left == access;

    private static bool IsInsideNameOf(SyntaxNode node) =>
        node.Ancestors().OfType<InvocationExpressionSyntax>().Any(call =>
            call.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });

    /// <summary>该读取是否直接作为三个白名单解析器之一的实参（允许 ?? 等表达式包裹）。</summary>
    private static bool FlowsIntoResolver(MemberAccessExpressionSyntax access)
    {
        var argument = access.FirstAncestorOrSelf<ArgumentSyntax>();
        if (argument?.Parent?.Parent is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => null,
        };

        return name is not null && WhitelistResolvers.Contains(name);
    }

    /// <summary>是否参与字符串插值或 <c>+</c> 拼接——注入面，无豁免。</summary>
    private static bool IsStringComposition(MemberAccessExpressionSyntax access)
    {
        for (SyntaxNode? node = access; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case InterpolationSyntax:
                    return true;
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                    return true;
                case StatementSyntax:
                case LambdaExpressionSyntax:
                    return false;
            }
        }

        return false;
    }

    private static string DescribeOwner(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case MethodDeclarationSyntax method:
                    return method.Identifier.ValueText;
                case PropertyDeclarationSyntax property:
                    return property.Identifier.ValueText;
                case ConstructorDeclarationSyntax ctor:
                    return $".ctor({ctor.Identifier.ValueText})";
            }
        }

        return "?";
    }

    private static HashSet<string> LoadBaseline()
    {
        var file = Path.Combine(
            LocateRepositoryRoot(),
            BaselineFileName.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(file).ShouldBeTrue($"未找到排序透传基线文件：{file}");

        return File.ReadAllLines(file)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string Compact(string snippet)
    {
        var flattened = string.Join(' ', snippet.Split('\n', '\r')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0));
        return flattened.Length <= 120 ? flattened : flattened[..117] + "...";
    }

    private static bool IsGenerated(string relPath) =>
        relPath.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase) ||
        relPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
        relPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
        relPath.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase) ||
        relPath.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase);

    private static string? LocateSrcDirectory()
    {
        var src = Path.Combine(LocateRepositoryRoot(), "src");
        return Directory.Exists(src) ? src : null;
    }

    private static string LocateRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SwapMid.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("未能定位 SwapMid.slnx");
    }
}
