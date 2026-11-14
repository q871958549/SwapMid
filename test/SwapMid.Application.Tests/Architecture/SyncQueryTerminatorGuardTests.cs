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
/// **同步查询终结符门禁**——挡住 <c>IQueryable</c> 上的 <c>.ToList()</c> / <c>.Count()</c> /
/// <c>.First()</c> 等同步终结符。
///
/// 为什么这是可用性问题而非性能问题：同步终结符会**阻塞线程池线程**直到数据库返回。
/// 单次调用变慢只是表象——真正的失效模式是数据量增长后，十几个并发用户就能抽干线程池，
/// 于是**整个 Host 的所有业务域**一起排队（不只是慢查询所在的那个页面），`/health-status`
/// 随之超时、容器被判死重启，形成雪崩。转异步**不会让查询变快**，它保证的是「慢的时候不塌」。
///
/// ## 为什么用纯语法而不做语义分析
/// 语义分析（<c>SemanticModel</c>）需要完整 <c>Compilation</c> + 全部程序集引用解析，慢且在
/// 多会话并行构建下脆弱。本仓既有架构守卫（<see cref="EtlStreamingBoundaryGuardTests"/>、
/// <see cref="FileLengthGuardTests"/>）一律纯语法，本测试保持一致。
///
/// ## 纯语法如何区分 `IQueryable.ToList()` 与 `List&lt;T&gt;.ToList()`
/// 这是本门禁唯一的技术难点：`foo.ToList()` 在语法树上看不出 `foo` 是什么类型，而内存集合上的
/// `.ToList()` 完全合法且遍地都是——无差别拦截会淹没在误报里。
///
/// 解法是**方法体内的局部数据流跟踪**：只从三类**语法可见**的 queryable 起源出发，
/// 沿链式调用传播，再检查传播所及之处有无同步终结符。
/// 1. `await xxx.GetQueryableAsync()` / `GetDbSetAsync()` / `WithDetailsAsync()` 的结果；
/// 2. 显式写出类型的变量/参数/字段（`IQueryable&lt;T&gt;` / `IOrderedQueryable&lt;T&gt;` / `DbSet&lt;T&gt;`）；
/// 3. `.AsQueryable()` 的结果。
///
/// 传播只经过<see cref="QueryablePreserving"/>里的操作符。这带来一个关键的**自动收敛**：
/// `AsEnumerable()` 不在该集合里，所以 `q.AsEnumerable().ToList()` 天然不告警——那正是
/// 「我确实要在内存里做」的标准写法。同理 `AsyncExecuter.ToListAsync(q)` 的接收者是
/// `AsyncExecuter` 而非 queryable 变量，也不会命中。
///
/// 代价是**会漏**（跨方法传递的 IQueryable、经字段中转的），不会**误伤**。门禁的价值在于挡住
/// 增量，漏掉的部分由 code review 和 P4 的慢查询指标兜底——宁可漏也不能误报，误报会让整个门禁
/// 被当噪音绕过。
/// </summary>
public class SyncQueryTerminatorGuardTests
{
    /// <summary>
    /// 同步终结符：一旦作用在 IQueryable 上就会发 SQL 并**阻塞当前线程**等结果。
    /// 对应的异步写法是 <c>AsyncExecuter.XxxAsync(query)</c>（Application 层，禁 using EF Core）
    /// 或 <c>await query.XxxAsync()</c>（EntityFrameworkCore 层）。
    /// </summary>
    private static readonly HashSet<string> SyncTerminators = new(StringComparer.Ordinal)
    {
        "ToList", "ToArray", "ToDictionary", "ToHashSet", "ToLookup",
        "Count", "LongCount", "Any", "All",
        "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
        "Sum", "Min", "Max", "Average",
        "Contains", "ElementAt", "ElementAtOrDefault",
    };

    /// <summary>
    /// 保持 IQueryable 语义的操作符——数据流沿这些调用继续传播。
    /// **刻意不含 <c>AsEnumerable</c> / <c>AsAsyncEnumerable</c>**：它们是「切到内存」的显式声明，
    /// 之后的 `.ToList()` 是合法内存操作，跟踪必须在此终止（见类注释）。
    /// </summary>
    private static readonly HashSet<string> QueryablePreserving = new(StringComparer.Ordinal)
    {
        "Where", "WhereIf", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
        "Select", "SelectMany", "Skip", "Take", "SkipWhile", "TakeWhile",
        "Distinct", "GroupBy", "Join", "GroupJoin", "DefaultIfEmpty", "Reverse",
        "Union", "Concat", "Intersect", "Except",
        "Include", "ThenInclude", "AsNoTracking", "AsNoTrackingWithIdentityResolution",
        "AsTracking", "IgnoreQueryFilters", "TagWith", "AsSplitQuery", "AsSingleQuery",
        // 平台共享件
        "ApplyWhitelistedSorting", "PageBy", "AsQueryable",
    };

    /// <summary>语法可见的 queryable 起源调用（见类注释第 1、3 类）。</summary>
    private static readonly HashSet<string> QueryableOrigins = new(StringComparer.Ordinal)
    {
        "GetQueryableAsync", "GetDbSetAsync", "WithDetailsAsync", "AsQueryable",
    };

    /// <summary>显式写出的 queryable 类型名（见类注释第 2 类）。</summary>
    private static readonly HashSet<string> QueryableTypeNames = new(StringComparer.Ordinal)
    {
        "IQueryable", "IOrderedQueryable", "DbSet",
    };

    /// <summary>
    /// 基线文件（相对仓库根）：现存的同步终结符调用点，每行一个 <c>相对src路径::方法名::终结符名</c>，
    /// <c>#</c> 开头为注释。
    ///
    /// **为什么放外部文件而不是 C# 字面量**：条目数以百计（首次扫描 236 条），塞进 HashSet 会让本测试
    /// 文件膨胀到读不动；独立文件还让基线的增减在 git diff 里单独可见——review 时一眼看出是「又欠了
    /// 一笔」还是「还了一笔」。
    ///
    /// **为什么 key 不含行号**：行号随无关编辑漂移会让基线天天失效。方法名 + 终结符名的粒度既能定位，
    /// 又只在真正重构时才需要维护。同一方法内同名终结符出现多次只记一条。
    /// </summary>
    private const string BaselineFileName =
        "test/SwapMid.Application.Tests/Architecture/sync-query-terminator-baseline.txt";

    [Fact]
    public void IQueryable上不应新增同步终结符()
    {
        var baseline = LoadBaseline();
        var offenders = Scan()
            .Where(hit => !baseline.Contains(hit.Key))
            .ToList();

        offenders.ShouldBeEmpty(
            "以下位置在 IQueryable 上调用了同步终结符，会阻塞线程池线程——数据量涨上来后拖垮的是\n" +
            "**整个 Host 的所有业务域**，不只是当前这个页面（见本类注释）。修法：\n" +
            "  · Application 层 → AsyncExecuter.XxxAsync(query)（该层禁 using Microsoft.EntityFrameworkCore）\n" +
            "  · EntityFrameworkCore 层 → await query.XxxAsync()\n" +
            "  · 确属内存操作 → 先 .AsEnumerable() 显式切换语义，本门禁即自动放行\n" +
            $"确有必要保留的，把下面的 key 行追加进 {BaselineFileName}：\n" +
            string.Join("\n", offenders.Select(hit => $"  {hit.Key}\n      ({hit.Location}) {hit.Snippet}")));
    }

    [Fact]
    public void 基线条目应真实存在_否则请删除豁免()
    {
        var live = Scan().Select(hit => hit.Key).ToHashSet(StringComparer.Ordinal);

        var stale = LoadBaseline().Where(entry => !live.Contains(entry)).ToList();

        stale.ShouldBeEmpty(
            $"以下基线条目已不存在（多半是已改成异步——干得漂亮）。基线只能随整改缩短，\n" +
            $"请从 {BaselineFileName} 删除这些行，防止它们日后为新债务提供掩护：\n" +
            string.Join("\n", stale.Select(entry => $"  {entry}")));
    }

    private static HashSet<string> LoadBaseline()
    {
        var file = Path.Combine(
            LocateRepositoryRoot(),
            BaselineFileName.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(file).ShouldBeTrue($"未找到同步终结符基线文件：{file}");

        return File.ReadAllLines(file)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
    }

    // ── 扫描器自检（元测试）──────────────────────────────────────────────────────────────
    //
    // 扫描器是本门禁的心脏，且它的取舍（哪些传播、哪些终止）无法从真实代码库反推验证：
    // 若有人误删 QueryablePreserving 里的条目，门禁会**静默失效**——不再报任何东西。
    //
    // 这里有一层天然的互相守护：扫描器变迟钝 → 236 条基线集体失效 → 「基线条目应真实存在」失败；
    // 扫描器变过敏 → 冒出未登记条目 → 主用例失败。但那只在**全库**尺度上生效，且失败信息指向
    // 一堆业务文件而非扫描器本身。下面的样本把关键边界钉死在最小可读的输入上，改坏时直接指认原因。

    [Theory]
    // 起源 1：仓储 queryable 上的同步终结符——最典型的命中形态。
    [InlineData("var list = (await _repo.GetQueryableAsync()).Where(x => x.Ok).ToList();", "M::ToList")]
    // 起源 2：显式声明的 IQueryable 变量。
    [InlineData("IQueryable<Foo> q = Source(); var n = q.Count();", "M::Count")]
    // 传播：多级派生后仍应识别。
    [InlineData("var q = await _repo.GetQueryableAsync(); var q2 = q.Where(x => x.Ok); var f = q2.FirstOrDefault();", "M::FirstOrDefault")]
    // 赋值传播：q = q.Where(...) 这种就地收窄。
    [InlineData("var q = await _repo.GetQueryableAsync(); q = q.Where(x => x.Ok); var n = q.Count();", "M::Count")]
    public void 扫描器应识别IQueryable上的同步终结符(string body, string expected)
    {
        ScanSnippet(body).ShouldContain(expected);
    }

    [Theory]
    // AsEnumerable() 是「我确实要在内存里做」的显式声明，跟踪必须在此终止。
    [InlineData("var list = (await _repo.GetQueryableAsync()).AsEnumerable().ToList();")]
    // 正确写法本身不该被报（Application 层）。
    [InlineData("var list = await AsyncExecuter.ToListAsync(await _repo.GetQueryableAsync());")]
    // 正确写法（EntityFrameworkCore 层）。
    [InlineData("var list = await (await _repo.GetQueryableAsync()).ToListAsync();")]
    // lambda 内对**内存集合**的 Contains/Any——最高频的误报陷阱：接收者是 ids，不是 queryable。
    [InlineData("var q = await _repo.GetQueryableAsync(); var r = await AsyncExecuter.ToListAsync(q.Where(x => ids.Contains(x.Id) && x.Tags.Any()));")]
    // 纯内存集合上的终结符：与查询无关，必须放行。
    [InlineData("var names = employees.Where(x => x.Ok).Select(x => x.Name).ToList(); var n = names.Count();")]
    // 🔴 **queryable 变量在 lambda 内被 .Any()**：这是 EXISTS 子查询、不是终结符，EF 整体翻译成 SQL。
    // 真实形态见 SampleVisibilityChecker.ApplyRelatedToMeAsync——首版扫描器把它误报了，
    // 而它恰好在一个「本域禁止同步打库」的域里，误报会直接摧毁门禁的可信度。
    [InlineData("var related = await _t.GetQueryableAsync(); var q = await _s.GetQueryableAsync(); var r = q.Where(x => related.Any(t => t.SampleId == x.Id));")]
    // 子查询里套 Contains 同理。
    [InlineData("var sub = await _t.GetQueryableAsync(); var q = await _s.GetQueryableAsync(); var r = q.Where(x => sub.Select(t => t.SampleId).Contains(x.Id));")]
    public void 扫描器不应误报非查询路径(string body)
    {
        ScanSnippet(body).ShouldBeEmpty();
    }

    private static IReadOnlyList<string> ScanSnippet(string body)
    {
        var source = $$"""
            using System.Linq;
            public class Probe
            {
                public async Task M()
                {
            {{body}}
                }
            }
            """;

        return ScanRoot(CSharpSyntaxTree.ParseText(source).GetRoot())
            .Select(hit => $"{hit.Owner}::{MemberName(hit.Call)}")
            .ToList();
    }

    // ── 扫描实现 ────────────────────────────────────────────────────────────────────────

    private readonly record struct Hit(string Key, string Location, string Snippet);

    private static IEnumerable<(string Owner, InvocationExpressionSyntax Call)> ScanRoot(SyntaxNode root)
    {
        foreach (var scope in EnumerateScopes(root))
        {
            var owner = DescribeScope(scope);
            foreach (var call in FindSyncTerminators(scope))
            {
                yield return (owner, call);
            }
        }
    }

    private static List<Hit> Scan()
    {
        var srcDir = LocateSrcDirectory();
        srcDir.ShouldNotBeNull("未能定位 src 目录（从测试运行目录向上找 SwapMid.slnx 失败）");

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

            foreach (var (owner, call) in ScanRoot(root))
            {
                var key = $"{rel}::{owner}::{MemberName(call)}";
                // 同一方法内同名终结符多次出现只报一次：基线豁免的语义是
                // 「这个方法里允许用这个终结符」，逐次登记会让基线随无关编辑抖动。
                if (!seen.Add(key))
                {
                    continue;
                }

                var line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                hits.Add(new Hit(key, $"{rel}:{line}", Compact(call.ToString())));
            }
        }

        return hits;
    }

    /// <summary>
    /// 变量作用域 = 有方法体的成员。局部函数与 lambda 天然嵌在其中，一并覆盖，
    /// 因此**不**单独枚举它们（否则同一命中会被父作用域和子作用域各报一次）。
    /// </summary>
    private static IEnumerable<SyntaxNode> EnumerateScopes(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case MethodDeclarationSyntax:
                case ConstructorDeclarationSyntax:
                case AccessorDeclarationSyntax:
                    yield return node;
                    break;
                // 表达式体属性（=> ...），没有 accessor 节点
                case PropertyDeclarationSyntax { ExpressionBody: not null }:
                    yield return node;
                    break;
            }
        }
    }

    private static string DescribeScope(SyntaxNode scope) => scope switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        ConstructorDeclarationSyntax ctor => $".ctor({ctor.Identifier.ValueText})",
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        AccessorDeclarationSyntax accessor =>
            $"{accessor.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "?"}" +
            $".{accessor.Keyword.ValueText}",
        _ => "?",
    };

    private static IEnumerable<InvocationExpressionSyntax> FindSyncTerminators(SyntaxNode scope)
    {
        var queryableVars = CollectQueryableVariables(scope);

        foreach (var call in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax member)
            {
                continue;
            }

            if (!SyncTerminators.Contains(member.Name.Identifier.ValueText))
            {
                continue;
            }

            // 🔴 lambda 内部的调用**不是**终结符——它是表达式树的一部分，由 EF 翻译成 SQL
            // 子查询（如 `Where(x => related.Any(t => t.SampleId == x.Id))` → EXISTS），
            // 不会在客户端立即执行、更不会阻塞线程。漏掉这条判断会把大量合法子查询报成违规，
            // 而误报正是门禁被当噪音绕过的头号原因（本类原则：宁可漏，不可误报）。
            // 代价：`Task.Run(() => query.ToList())` 这类「真在 lambda 里同步执行」会漏——极罕见，
            // 且它的危害由 P4 的慢查询指标兜底。
            if (IsInsideLambda(call))
            {
                continue;
            }

            if (IsQueryable(member.Expression, queryableVars))
            {
                yield return call;
            }
        }
    }

    /// <summary>调用是否位于 lambda / 匿名方法体内（到成员声明边界为止）。</summary>
    private static bool IsInsideLambda(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                return true;
            }

            if (current is MemberDeclarationSyntax or AccessorDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// 收集本作用域内确定是 IQueryable 的标识符。按源码位置顺序处理，使
    /// <c>var q2 = q.Where(...)</c> 这类逐级派生能正确传播（C# 要求先声明后使用）。
    /// </summary>
    private static HashSet<string> CollectQueryableVariables(SyntaxNode scope)
    {
        var vars = new HashSet<string>(StringComparer.Ordinal);

        // 参数：类型显式写成 IQueryable<T> / IOrderedQueryable<T> / DbSet<T>
        foreach (var parameter in scope.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (parameter.Type is not null && IsQueryableType(parameter.Type))
            {
                vars.Add(parameter.Identifier.ValueText);
            }
        }

        var nodes = scope.DescendantNodes()
            .Where(node => node is VariableDeclaratorSyntax or AssignmentExpressionSyntax)
            .OrderBy(node => node.SpanStart);

        foreach (var node in nodes)
        {
            switch (node)
            {
                case VariableDeclaratorSyntax declarator:
                {
                    var declaredType = (declarator.Parent as VariableDeclarationSyntax)?.Type;
                    if (declaredType is not null && IsQueryableType(declaredType))
                    {
                        vars.Add(declarator.Identifier.ValueText);
                        break;
                    }

                    if (declarator.Initializer is not null &&
                        IsQueryable(declarator.Initializer.Value, vars))
                    {
                        vars.Add(declarator.Identifier.ValueText);
                    }

                    break;
                }

                case AssignmentExpressionSyntax assignment
                    when assignment.Left is IdentifierNameSyntax target:
                {
                    if (IsQueryable(assignment.Right, vars))
                    {
                        vars.Add(target.Identifier.ValueText);
                    }

                    break;
                }
            }
        }

        return vars;
    }

    private static bool IsQueryableType(TypeSyntax type)
    {
        var name = type switch
        {
            GenericNameSyntax generic => generic.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right switch
            {
                GenericNameSyntax generic => generic.Identifier.ValueText,
                var right => right.Identifier.ValueText,
            },
            NullableTypeSyntax nullable => IsQueryableType(nullable.ElementType) ? "IQueryable" : null,
            _ => null,
        };

        return name is not null && QueryableTypeNames.Contains(name);
    }

    /// <summary>表达式是否求值为 IQueryable——沿链式调用回溯到某个已知起源。</summary>
    private static bool IsQueryable(ExpressionSyntax expression, HashSet<string> queryableVars)
    {
        switch (expression)
        {
            case IdentifierNameSyntax identifier:
                return queryableVars.Contains(identifier.Identifier.ValueText);

            case AwaitExpressionSyntax awaited:
                return IsQueryable(awaited.Expression, queryableVars);

            case ParenthesizedExpressionSyntax parenthesized:
                return IsQueryable(parenthesized.Expression, queryableVars);

            case PostfixUnaryExpressionSyntax postfix
                when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                return IsQueryable(postfix.Operand, queryableVars);

            case InvocationExpressionSyntax call:
            {
                var name = MemberName(call);
                if (name is null)
                {
                    return false;
                }

                if (QueryableOrigins.Contains(name))
                {
                    return true;
                }

                // 只经保持 queryable 语义的操作符传播；AsEnumerable() 等不在集合里，跟踪在此终止。
                return QueryablePreserving.Contains(name) &&
                       call.Expression is MemberAccessExpressionSyntax member &&
                       IsQueryable(member.Expression, queryableVars);
            }

            default:
                return false;
        }
    }

    private static string? MemberName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            _ => null,
        };

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
