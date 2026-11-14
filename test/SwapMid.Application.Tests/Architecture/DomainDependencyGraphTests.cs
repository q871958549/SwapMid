using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

/// <summary>
/// **业务域依赖图棘轮**（docs/structure-health-review-2026-11-03.md §3.2 A1 · 补全 ADR-0009「架构测试防腐」）。
///
/// <see cref="DomainDependencyTests"/> 与 <see cref="SharedKernelBoundaryTests"/> 是点状规则：只管共享内核、四大方向、
/// Enterprise↔Account 与「进入 EnterpriseManagement 的入边」。业务域之间的其余依赖此前完全不受约束——
/// 2026-09 EnterpriseManagement 直接注入 ProjectManagement 具体类，就是从这道缝里长出来的。
///
/// 本类把**全部**「业务域 → 业务域」的边锁进 <see cref="BaselineFileName"/>：新增边即红；已登记的边修掉而未删登记也红
/// （单调棘轮，只准变短）。双向依赖另锁在 <see cref="KnownMutualPairs"/>——**环只准拆，不接受新登记**。
///
/// ## 边从哪来（两路取并集）
/// 1. **IL 级**（Mono.Cecil，随 NetArchTest 传递引入）：读 Domain.Shared / Domain / Application.Contracts / Application 四个程序集，
///    遍历每个类型的基类、接口、泛型约束、字段、属性、事件、方法签名、局部变量、方法体指令操作数与特性（含 <c>typeof</c> 参数）。
///    能看到部分限定名（命名空间内写 <c>ProjectManagement.Foo</c>，不需要 using）。
/// 2. **源码级**（Roslyn 语法树）：显式写出的 <c>SwapMid.{域}</c>——using / using static / 别名 / 全限定名。
///    补 IL 的盲区：<c>const</c> 与枚举字面量编译期内联，IL 里不留引用，但源码耦合是真的。
///    代价：未使用的 using 也算边（本仓未强制 IDE0005），删掉它即可消边。注释、cref、字符串不算。
///
/// 「域」= <c>SwapMid.</c> 之后的第一段命名空间；嵌套类型（含编译器生成的状态机 / 闭包）归最外层声明类型。
/// 根命名空间（模块装配、Mapper 汇总）与 <see cref="NonBusinessSegments"/> 是组合根或横切底座，不进业务图。
///
/// ## 报红了怎么办
/// 先问「这条依赖能不能反过来」：被依赖方发本地事件、提到 SharedKernel 契约、或把类型挪到真正拥有它的域。
/// 确需新增时把行加进基线，PR 描述里写明理由——基线是台账不是许可证；会形成双向依赖的边连登记都过不了。
/// </summary>
public class DomainDependencyGraphTests
{
    private const string Root = "SwapMid.";

    private const string BaselineFileName =
        "test/SwapMid.Application.Tests/Architecture/domain-dependency-baseline.txt";

    private const string ProbeRoot = "SwapMid.ArchitectureProbes.";

    /// <summary>源码级扫描的项目目录，与 IL 级扫描的四个程序集一一对应。</summary>
    private static readonly string[] ScannedProjects =
    {
        "src/SwapMid.Domain.Shared",
        "src/SwapMid.Domain",
        "src/SwapMid.Application.Contracts",
        "src/SwapMid.Application",
    };

    /// <summary>
    /// 共享内核段（ADR-0009）：只能被依赖。前五个与 <see cref="DomainDependencyTests"/> 同一口径；
    /// Scheduling 按 ADR-0024 登记为底座（引擎 + Hangfire 入口 + 横切清理任务），业务 Handler 一律放所属域 <c>{域}/Jobs/</c>。
    /// </summary>
    private static readonly HashSet<string> SharedKernelSegments = new(StringComparer.Ordinal)
    {
        "Infrastructure", "Settings", "Notification", "Monitoring", "SharedKernel", "Scheduling",
    };

    /// <summary>非业务段 = 共享内核 + 框架 / 平台横切（权限常量、本地化资源、种子装配、ABP Identity / OpenIddict / BlobStoring 定制）。</summary>
    private static readonly HashSet<string> NonBusinessSegments = new(
        SharedKernelSegments.Concat(new[]
        {
            "Permissions", "Localization", "Data", "Identity", "OpenIddict", "BlobStoring", "Security",
        }),
        StringComparer.Ordinal);

    /// <summary>
    /// 现存双向依赖，"A &lt;-&gt; B" 按序数排序。**只准变短**：拆掉一个环就删一行。
    /// 新出现的环不能靠往这里加一行过关——环意味着两个域已经无法分开理解、分开修改。
    /// </summary>
    private static readonly HashSet<string> KnownMutualPairs = new(StringComparer.Ordinal)
    {
        // 2026-11-04：原 4 对「X <-> Scheduling」随 Scheduling 登记为共享内核段（ADR-0024）退出业务图——
        // 业务域依赖引擎是正当方向，反方向改由 SharedKernelExceptions 逐边棘轮。
        // 2026-11-04 S2-6：数据字典三对环拆除（业务字典种子迁回各域、受保护字典改由 IDataDictionaryOwnerPolicy 声明）。保持空集。
    };

    /// <summary>
    /// 共享内核段对业务域的已知依赖。Infrastructure / SharedKernel 已由 <see cref="DomainDependencyTests"/> 零例外断言，
    /// 这里补上此前列入共享内核却免检的 Notification / Monitoring / Settings。只准变短。
    /// </summary>
    private static readonly HashSet<string> SharedKernelExceptions = new(StringComparer.Ordinal)
    {
        // Notification / Monitoring / Settings：2026-11-03 B0 清零（指标标签映射挪回各业务域、设置定义按域拆 provider、
        // RS05 回执服务归位 Out），保持空集。
        // Scheduling：2026-11-04 ADR-0024 四批迁完（业务 Handler 归各域 {域}/Jobs/），清零。保持空集。
    };

    private static readonly string[] SharedKernelSegmentsUnderGraphGuard = { "Notification", "Monitoring", "Settings", "Scheduling" };

    private static readonly Lazy<Snapshot> Live = new(TakeSnapshot);

    [Fact]
    public void 业务域间依赖不应超出基线()
    {
        var live = Live.Value;
        var baseline = LoadBaseline();
        var liveEdges = live.BusinessEdges.Keys.ToHashSet(StringComparer.Ordinal);

        var added = live.BusinessEdges.Keys.Where(edge => !baseline.Contains(edge)).OrderBy(e => e, StringComparer.Ordinal).ToList();

        added.ShouldBeEmpty(
            "以下业务域依赖不在基线里。先问能否反过来（发本地事件 / 提到 SharedKernel 契约 / 把类型挪到拥有它的域）；\n" +
            $"确需新增，把行追加进 {BaselineFileName}，并在 PR 描述里写明理由：\n" +
            string.Join("\n", added.Select(edge => Describe(edge, live.BusinessEdges[edge], liveEdges))));
    }

    [Fact]
    public void 基线中的依赖边应真实存在_否则请删除()
    {
        var liveEdges = Live.Value.BusinessEdges.Keys.ToHashSet(StringComparer.Ordinal);

        var stale = LoadBaseline().Where(edge => !liveEdges.Contains(edge)).OrderBy(e => e, StringComparer.Ordinal).ToList();

        stale.ShouldBeEmpty(
            $"以下依赖已经不存在。基线只能随整改缩短，请从 {BaselineFileName} 删除：\n" +
            string.Join("\n", stale.Select(edge => $"  {edge}")));
    }

    [Fact]
    public void 不应新增双向依赖()
    {
        var added = MutualPairs(Live.Value.BusinessEdges.Keys).Where(pair => !KnownMutualPairs.Contains(pair)).ToList();

        added.ShouldBeEmpty(
            "以下两个业务域互相依赖。环不接受登记——拆掉其中一个方向（通常是让底层域发事件、上层域订阅）：\n" +
            string.Join("\n", added.Select(pair => $"  {pair}")));
    }

    [Fact]
    public void 已拆除的双向依赖应从KnownMutualPairs删除()
    {
        var live = MutualPairs(Live.Value.BusinessEdges.Keys).ToHashSet(StringComparer.Ordinal);

        var stale = KnownMutualPairs.Where(pair => !live.Contains(pair)).OrderBy(p => p, StringComparer.Ordinal).ToList();

        stale.ShouldBeEmpty(
            $"以下环已经拆掉，请从 {nameof(KnownMutualPairs)} 删除，防止回退：\n" +
            string.Join("\n", stale.Select(pair => $"  {pair}")));
    }

    [Fact]
    public void 共享内核段_Notification_Monitoring_Settings_Scheduling_不应依赖业务域()
    {
        var live = Live.Value;
        var violations = live.SharedKernelEdges.Keys.Where(edge => !SharedKernelExceptions.Contains(edge)).OrderBy(e => e, StringComparer.Ordinal).ToList();

        violations.ShouldBeEmpty(
            "共享内核只能被依赖（ADR-0009）。以下依赖让底座反过来认识了业务域，请把逻辑挪回业务域、由业务域调用共享内核：\n" +
            string.Join("\n", violations.Select(edge => Describe(edge, live.SharedKernelEdges[edge], null))));
    }

    [Fact]
    public void 共享内核例外条目应真实存在_否则请删除()
    {
        var live = Live.Value.SharedKernelEdges.Keys.ToHashSet(StringComparer.Ordinal);

        var stale = SharedKernelExceptions.Where(edge => !live.Contains(edge)).OrderBy(e => e, StringComparer.Ordinal).ToList();

        stale.ShouldBeEmpty(
            $"以下例外已经不存在，请从 {nameof(SharedKernelExceptions)} 删除：\n" +
            string.Join("\n", stale.Select(edge => $"  {edge}")));
    }

    [Fact]
    public void IL扫描不应有无法解析的特性参数()
    {
        // 特性实参里的枚举需要解析类型才能读出；解析失败会让该特性的 typeof 参数一并漏扫，必须可见。
        Live.Value.Unresolved.ShouldBeEmpty(
            "以下特性的实参未能解析，其中的类型依赖会被漏扫。请为 IL 扫描补充程序集搜索目录：\n" +
            string.Join("\n", Live.Value.Unresolved.Select(item => $"  {item}")));
    }

    // ── 扫描器自检 ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ViaField")]
    [InlineData("ViaBaseType")]
    [InlineData("ViaGenericArgument")]
    [InlineData("ViaMethodBody")]
    [InlineData("ViaAsyncStateMachine")]
    [InlineData("ViaAttributeTypeof")]
    public void IL扫描器应识别各类引用(string probe)
    {
        ProbeEdges.Value.ShouldContain($"{probe} -> Target");
    }

    [Theory]
    [InlineData("ConstOnly")] // 已知盲区：const 内联，由源码级扫描兜底
    [InlineData("Isolated")]
    public void IL扫描器不应凭空制造引用(string probe)
    {
        ProbeEdges.Value.ShouldNotContain($"{probe} -> Target");
    }

    [Theory]
    [InlineData("using SwapMid.Target;\nnamespace SwapMid.Source;\nclass P { }")]
    [InlineData("namespace SwapMid.Source;\nusing static SwapMid.Target.Consts;\nclass P { }")]
    [InlineData("using Alias = SwapMid.Target.Foo;\nnamespace SwapMid.Source;\nclass P { }")]
    [InlineData("namespace SwapMid.Source;\nclass P { string C => global::SwapMid.Target.Consts.Code; }")]
    [InlineData("namespace SwapMid.Source.Sub\n{\n    class P { SwapMid.Target.Foo? F; }\n}")]
    public void 源码扫描器应识别显式写出的域引用(string source)
    {
        ScanSourceText(source).ShouldBe(new[] { ("Source", "Target") });
    }

    [Theory]
    // 注释、cref、字符串不是依赖。
    [InlineData("namespace SwapMid.Source;\n/// <see cref=\"SwapMid.Target.Foo\"/>\nclass P { string S = \"SwapMid.Target\"; }")]
    // 同域引用不是边；自身的命名空间声明不是引用。
    [InlineData("using SwapMid.Source.Other;\nnamespace SwapMid.Source.Sub;\nclass P { }")]
    // 根命名空间不是域。
    [InlineData("using SwapMid;\nnamespace SwapMid.Source;\nclass P { }")]
    public void 源码扫描器不应误报(string source)
    {
        ScanSourceText(source).ShouldBeEmpty();
    }

    // ── 快照 ─────────────────────────────────────────────────────────────────────────────

    private sealed record Snapshot(
        IReadOnlyDictionary<string, SortedSet<string>> BusinessEdges,
        IReadOnlyDictionary<string, SortedSet<string>> SharedKernelEdges,
        IReadOnlyList<string> Unresolved);

    private static Snapshot TakeSnapshot()
    {
        var all = new Dictionary<(string From, string To), SortedSet<string>>();
        var unresolved = new List<string>();
        var assemblies = new[]
        {
            typeof(SwapMidDomainSharedModule).Assembly.Location,
            typeof(SwapMidDomainModule).Assembly.Location,
            typeof(SwapMidApplicationContractsModule).Assembly.Location,
            typeof(SwapMidApplicationModule).Assembly.Location,
        };

        var declared = ScanIl(assemblies, Root, all, unresolved);

        var repoRoot = LocateRepositoryRoot();
        foreach (var project in ScannedProjects)
        {
            var dir = Path.Combine(repoRoot, project.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("/Migrations/"))
                {
                    continue;
                }

                foreach (var (from, to) in ScanSourceText(File.ReadAllText(file)))
                {
                    // 全限定写到根命名空间下的类型（SwapMid.XxxErrorCodes）时第二段是类型名而非域。
                    if (declared.Contains(to))
                    {
                        AddEvidence(all, from, to, "src " + rel);
                    }
                }
            }
        }

        var business = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var sharedKernel = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var ((from, to), evidence) in all)
        {
            if (NonBusinessSegments.Contains(to))
            {
                continue;
            }

            if (!NonBusinessSegments.Contains(from))
            {
                business[$"{from} -> {to}"] = evidence;
            }
            else if (SharedKernelSegmentsUnderGraphGuard.Contains(from))
            {
                sharedKernel[$"{from} -> {to}"] = evidence;
            }
        }

        return new Snapshot(business, sharedKernel, unresolved);
    }

    private static readonly Lazy<HashSet<string>> ProbeEdges = new(() =>
    {
        var edges = new Dictionary<(string From, string To), SortedSet<string>>();
        ScanIl(new[] { typeof(DomainDependencyGraphTests).Assembly.Location }, ProbeRoot, edges, new List<string>());
        return edges.Keys.Select(edge => $"{edge.From} -> {edge.To}").ToHashSet(StringComparer.Ordinal);
    });

    private static IEnumerable<string> MutualPairs(IEnumerable<string> edges)
    {
        var set = edges.ToHashSet(StringComparer.Ordinal);
        foreach (var edge in set)
        {
            var (from, to) = Split(edge);
            if (string.CompareOrdinal(from, to) < 0 && set.Contains($"{to} -> {from}"))
            {
                yield return $"{from} <-> {to}";
            }
        }
    }

    private static string Describe(string edge, SortedSet<string> evidence, HashSet<string>? liveEdges)
    {
        var (from, to) = Split(edge);
        var mutual = liveEdges != null && liveEdges.Contains($"{to} -> {from}")
            ? "（⚠️ 反向边已存在，会形成双向依赖——登记进基线也过不了「不应新增双向依赖」）"
            : string.Empty;
        var shown = evidence.Take(3).Select(e => $"      {e}");
        var more = evidence.Count > 3 ? new[] { $"      …另 {evidence.Count - 3} 处" } : Array.Empty<string>();
        return $"  {edge}{mutual}\n" + string.Join("\n", shown.Concat(more));
    }

    private static (string From, string To) Split(string edge)
    {
        var parts = edge.Split(" -> ");
        return (parts[0], parts[1]);
    }

    private static void AddEvidence(
        Dictionary<(string From, string To), SortedSet<string>> edges, string from, string to, string evidence)
    {
        if (from == to)
        {
            return;
        }

        if (!edges.TryGetValue((from, to), out var set))
        {
            edges[(from, to)] = set = new SortedSet<string>(StringComparer.Ordinal);
        }

        set.Add(evidence);
    }

    // ── IL 级扫描 ────────────────────────────────────────────────────────────────────────

    /// <summary>扫描程序集，把边写入 <paramref name="edges"/>，返回见到的全部域段（用于过滤源码扫描的误识别）。</summary>
    private static HashSet<string> ScanIl(
        IEnumerable<string> assemblyPaths,
        string prefix,
        Dictionary<(string From, string To), SortedSet<string>> edges,
        List<string> unresolved)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var paths = assemblyPaths.ToList();
        using var resolver = new DefaultAssemblyResolver();
        foreach (var dir in ResolverSearchDirectories(paths))
        {
            resolver.AddSearchDirectory(dir);
        }

        foreach (var path in paths)
        {
            using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
            foreach (var type in module.GetTypes())
            {
                var from = SegmentOf(type, prefix);
                if (from is null)
                {
                    continue;
                }

                declared.Add(from);
                var collector = new IlReferenceCollector(prefix, unresolved);
                collector.Visit(type);
                var evidence = "IL " + Outermost(type).FullName;
                foreach (var to in collector.Segments)
                {
                    AddEvidence(edges, from, to, evidence);
                }
            }
        }

        return declared;
    }

    /// <summary>
    /// 枚举型特性实参要解析到类型定义才能读出。共享框架（ASP.NET Core 等）不复制到测试输出目录，
    /// 故除输出目录外，还把运行时所在 shared 目录下的各框架版本目录都加进来。
    /// </summary>
    private static IEnumerable<string> ResolverSearchDirectories(IEnumerable<string> assemblyPaths)
    {
        var dirs = new List<string> { AppContext.BaseDirectory };
        dirs.AddRange(assemblyPaths.Select(Path.GetDirectoryName).OfType<string>());

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        var sharedDir = runtimeDir == null ? null : Directory.GetParent(runtimeDir)?.Parent;
        if (sharedDir is { Exists: true })
        {
            dirs.AddRange(sharedDir.EnumerateDirectories().SelectMany(framework => framework.EnumerateDirectories()).Select(d => d.FullName));
        }
        else if (runtimeDir != null)
        {
            dirs.Add(runtimeDir);
        }

        return dirs.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static TypeReference Outermost(TypeReference type)
    {
        while (type.DeclaringType != null)
        {
            type = type.DeclaringType;
        }

        return type;
    }

    private static string? SegmentOf(TypeReference type, string prefix) => SegmentOfNamespace(Outermost(type).Namespace, prefix);

    private static string? SegmentOfNamespace(string? ns, string prefix)
    {
        if (ns is null || ns.Length <= prefix.Length || !ns.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = ns.Substring(prefix.Length);
        var dot = rest.IndexOf('.');
        return dot < 0 ? rest : rest[..dot];
    }

    /// <summary>收集一个类型定义引用到的全部域段。覆盖面对齐 NetArchTest 的依赖搜索，但按命名空间段精确匹配（避免 Account 误中 AccountManagement）。</summary>
    private sealed class IlReferenceCollector(string prefix, List<string> unresolved)
    {
        public HashSet<string> Segments { get; } = new(StringComparer.Ordinal);

        public void Visit(TypeDefinition type)
        {
            Type(type.BaseType);
            foreach (var implementation in type.Interfaces)
            {
                Type(implementation.InterfaceType);
            }

            GenericParameters(type);
            Attributes(type);
            foreach (var field in type.Fields)
            {
                Type(field.FieldType);
                Attributes(field);
            }

            foreach (var property in type.Properties)
            {
                Type(property.PropertyType);
                Attributes(property);
            }

            foreach (var @event in type.Events)
            {
                Type(@event.EventType);
                Attributes(@event);
            }

            foreach (var method in type.Methods)
            {
                Method(method);
                Attributes(method);
                Attributes(method.MethodReturnType);
                foreach (var parameter in method.Parameters)
                {
                    Attributes(parameter);
                }

                GenericParameters(method);
                foreach (var overridden in method.Overrides)
                {
                    Method(overridden);
                }

                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var variable in method.Body.Variables)
                {
                    Type(variable.VariableType);
                }

                foreach (var handler in method.Body.ExceptionHandlers)
                {
                    Type(handler.CatchType);
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case TypeReference operandType:
                            Type(operandType);
                            break;
                        case MethodReference operandMethod:
                            Method(operandMethod);
                            break;
                        case FieldReference operandField:
                            Type(operandField.DeclaringType);
                            Type(operandField.FieldType);
                            break;
                    }
                }
            }
        }

        private void Type(TypeReference? type)
        {
            switch (type)
            {
                case null:
                case GenericParameter:
                    return;
                case GenericInstanceType generic:
                    Type(generic.ElementType);
                    foreach (var argument in generic.GenericArguments)
                    {
                        Type(argument);
                    }

                    return;
                case TypeSpecification specification: // 数组 / ByRef / 指针 / 修饰符
                    Type(specification.ElementType);
                    return;
            }

            var segment = SegmentOf(type, prefix);
            if (segment != null)
            {
                Segments.Add(segment);
            }
        }

        private void Method(MethodReference method)
        {
            Type(method.DeclaringType);
            Type(method.ReturnType);
            foreach (var parameter in method.Parameters)
            {
                Type(parameter.ParameterType);
            }

            if (method is GenericInstanceMethod generic)
            {
                foreach (var argument in generic.GenericArguments)
                {
                    Type(argument);
                }
            }
        }

        private void GenericParameters(IGenericParameterProvider provider)
        {
            foreach (var parameter in provider.GenericParameters)
            {
                foreach (var constraint in parameter.Constraints)
                {
                    Type(constraint.ConstraintType);
                }
            }
        }

        private void Attributes(ICustomAttributeProvider provider)
        {
            if (!provider.HasCustomAttributes)
            {
                return;
            }

            foreach (var attribute in provider.CustomAttributes)
            {
                Type(attribute.AttributeType);
                try
                {
                    foreach (var argument in attribute.ConstructorArguments)
                    {
                        Argument(argument);
                    }

                    foreach (var named in attribute.Properties.Concat(attribute.Fields))
                    {
                        Argument(named.Argument);
                    }
                }
                catch (AssemblyResolutionException ex)
                {
                    unresolved.Add($"{attribute.AttributeType.FullName} @ {provider}: {ex.AssemblyReference.Name}");
                }
            }
        }

        private void Argument(CustomAttributeArgument argument)
        {
            Type(argument.Type);
            switch (argument.Value)
            {
                case TypeReference typeValue:
                    Type(typeValue);
                    break;
                case CustomAttributeArgument boxed: // object 形参
                    Argument(boxed);
                    break;
                case CustomAttributeArgument[] items:
                    foreach (var item in items)
                    {
                        Argument(item);
                    }

                    break;
            }
        }
    }

    // ── 源码级扫描 ───────────────────────────────────────────────────────────────────────

    /// <summary>找显式写出的 <c>SwapMid.{域}</c>，返回去重后的 (源域, 目标域)，不含自引用。</summary>
    private static IReadOnlyList<(string From, string To)> ScanSourceText(string text)
    {
        var rootIdentifier = Root.TrimEnd('.');
        var root = CSharpSyntaxTree.ParseText(text).GetRoot();
        var fileSegments = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
            .Select(NamespaceSegment).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var results = new List<(string From, string To)>();

        foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText != rootIdentifier)
            {
                continue;
            }

            Microsoft.CodeAnalysis.SyntaxNode node = identifier;
            if (node.Parent is AliasQualifiedNameSyntax alias && alias.Name == node)
            {
                node = alias; // global::SwapMid
            }

            var to = node.Parent switch
            {
                QualifiedNameSyntax qualified when qualified.Left == node => qualified.Right.Identifier.ValueText,
                MemberAccessExpressionSyntax access when access.Expression == node => access.Name.Identifier.ValueText,
                _ => null,
            };
            if (to is null)
            {
                continue;
            }

            // 自身的命名空间声明不是引用。
            if (node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Any(ns => ns.Name.Span.Contains(node.Span)))
            {
                continue;
            }

            var enclosing = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
            var sources = enclosing != null
                ? new[] { NamespaceSegment(enclosing) }.OfType<string>()
                : fileSegments; // 文件顶部的 using 作用于本文件声明的全部命名空间

            foreach (var from in sources)
            {
                if (from != to && !results.Contains((from, to)))
                {
                    results.Add((from, to));
                }
            }
        }

        return results;
    }

    private static string? NamespaceSegment(BaseNamespaceDeclarationSyntax declaration)
    {
        var names = declaration.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(ns => ns.Name.ToString());
        return SegmentOfNamespace(string.Join(".", names), Root);
    }

    // ── 基线 ─────────────────────────────────────────────────────────────────────────────

    private static HashSet<string> LoadBaseline()
    {
        var file = Path.Combine(LocateRepositoryRoot(), BaselineFileName.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(file).ShouldBeTrue($"未找到业务域依赖基线文件：{file}");

        return File.ReadAllLines(file)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
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
