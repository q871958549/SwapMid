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
/// 批量端点必须集合化的门禁（2026-08-06）。
///
/// 拦的是这个形状——它在满载 2000 条时是 **4000 次数据库往返**，批量改状态还额外是
/// **O(N²)** 的 `DetectChanges`（Modified 实体保存后留在 ChangeTracker，每次 SaveChanges 都重扫全量）：
///
/// <code>
/// foreach (var id in input.Ids.Distinct()) {
///     var e = await Repository.FindAsync(id);
///     await Repository.DeleteAsync(e, autoSave: true);   // ← 每条一次 SaveChanges
/// }
/// </code>
///
/// 正解是 `SharedKernel/BatchOperationExecutor` 的 `DeleteAsync` / `DeleteExceptAsync` / `UpdateAsync`。
///
/// ## 放行判据：循环体里有没有 try/catch
///
/// **逐条 try/catch 容错的批量操作是合法的**，它们逐条独立正是设计意图——每条都带不可回滚的外部副作用
/// （建账号、发邮件、写 OA），一条失败不能拖垮整批，失败原因还要逐条回给用户。
/// 所以本守卫**只拦没有 try/catch 的**：那种循环没有任何逐条容错诉求，纯粹是没用集合化写法。
///
/// 这条判据刻意不做成基线文件——它是**语义自解释**的（读循环体就知道该不该拦），
/// 不像「哪些同步终结符待偿还」那样需要逐项登记。真要加逐条容错就得真写 try/catch，
/// 而写了就说明作者确实想过失败该怎么办。
/// </summary>
public class BatchSetBasedGuardTests
{
    [Fact]
    public void 批量端点不得逐条循环写库()
    {
        var applicationRoot = Path.Combine(FindSolutionRoot(), "src", "SwapMid.Application");
        Directory.Exists(applicationRoot).ShouldBeTrue($"未找到 Application 源码目录：{applicationRoot}");

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories))
        {
            // obj/bin 下是生成物（AssemblyInfo、Razor 中间产物等），不是人写的源码。
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var line in FindViolationLines(File.ReadAllText(file)))
            {
                violations.Add($"{Path.GetRelativePath(applicationRoot, file)}:{line}");
            }
        }

        violations.ShouldBeEmpty(
            "以下批量循环仍在逐条写库（每条一次 SaveChanges，2000 条即 4000 次往返；批量改状态还是 O(N²) 的 DetectChanges）。"
            + $"请改用 SharedKernel/BatchOperationExecutor：{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations)
            + $"{Environment.NewLine}确需逐条容错（每条带不可回滚的外部副作用、失败原因要逐条回给用户）的，"
            + "在循环体内写真实的 try/catch 兜底并把原因写进 result.Messages。");
    }

    /// <summary>
    /// 守卫的负例：**没有这组用例，上面那条主断言可能只是恒真**——
    /// 判定逻辑一旦写错（比如 `Ids` 拼错、`autoSave` 具名实参判反），主断言会安静地全绿，
    /// 而它本该拦的东西一个都拦不住。守卫必须自证「在该报的时候真的报」。
    /// </summary>
    [Theory]
    [InlineData("逐条删除应被拦", true, """
        foreach (var id in input.Ids.Distinct()) {
            var e = await Repository.FindAsync(id);
            await Repository.DeleteAsync(e, autoSave: true);
        }
        """)]
    [InlineData("逐条改状态应被拦", true, """
        foreach (var id in input.Ids) {
            var e = await Repository.FindAsync(id);
            e.Enable();
            await Repository.UpdateAsync(e, autoSave: true);
        }
        """)]
    [InlineData("逐条 try-catch 容错放行（形态 B）", false, """
        foreach (var id in input.Ids.Distinct()) {
            try {
                await _provisioner.ProvisionAsync(id);
                await Repository.UpdateAsync(e, autoSave: true);
            } catch (Exception ex) {
                result.Messages.Add(ex.Message);
            }
        }
        """)]
    [InlineData("循环体不写库则放行", false, """
        foreach (var id in input.Ids) {
            var e = await Repository.FindAsync(id);
            dtos.Add(ToDto(e));
        }
        """)]
    [InlineData("遍历的不是请求 id 集合则放行", false, """
        foreach (var item in input.Rows) {
            await Repository.InsertAsync(item, autoSave: true);
        }
        """)]
    public void 守卫判定逻辑(string because, bool shouldFlag, string body)
    {
        var source = $$"""
            class Probe { async Task RunAsync() { {{body}} } }
            """;

        FindViolationLines(source).Any().ShouldBe(shouldFlag, because);
    }

    /// <summary>扫一段源码，返回违规的 `foreach` 所在行号（1-based）。</summary>
    private static IReadOnlyList<int> FindViolationLines(string sourceText)
    {
        var root = CSharpSyntaxTree.ParseText(sourceText).GetRoot();
        var lines = new List<int>();

        foreach (var loop in root.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (!IteratesOverRequestedIds(loop) || !WritesPerIteration(loop))
            {
                continue;
            }

            // 逐条 try/catch = 形态 B，合法（见类注释）。
            if (loop.Statement.DescendantNodes().OfType<TryStatementSyntax>().Any())
            {
                continue;
            }

            lines.Add(loop.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
        }

        return lines;
    }

    /// <summary>循环对象是不是请求传进来的 id 集合（<c>input.Ids</c> / <c>input.Ids.Distinct()</c> …）。</summary>
    private static bool IteratesOverRequestedIds(ForEachStatementSyntax loop)
        => loop.Expression.DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(m => m.Name.Identifier.ValueText is "Ids");

    /// <summary>
    /// 循环体里有没有「每轮各写一次库」的调用。
    /// 判据取 <c>autoSave: true</c> 具名实参——ABP 仓储写方法就是靠它触发 `SaveChanges` 的，
    /// 比匹配方法名稳（`DeleteAsync` 可能是删子表的按条件重载，那种一次就删完、不算逐条）。
    /// </summary>
    private static bool WritesPerIteration(ForEachStatementSyntax loop)
        => loop.Statement.DescendantNodes()
            .OfType<ArgumentSyntax>()
            .Any(a => a.NameColon?.Name.Identifier.ValueText == "autoSave"
                      && a.Expression.IsKind(SyntaxKind.TrueLiteralExpression));

    private static string FindSolutionRoot()
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
