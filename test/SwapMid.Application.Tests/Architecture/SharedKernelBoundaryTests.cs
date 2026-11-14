using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

public class SharedKernelBoundaryTests
{
    private static readonly Assembly[] AllBusinessAssemblies =
    {
        typeof(SwapMidDomainSharedModule).Assembly,
        typeof(SwapMidDomainModule).Assembly,
        typeof(SwapMidApplicationContractsModule).Assembly,
        typeof(SwapMidApplicationModule).Assembly,
    };

    private static readonly HashSet<string> DomainsDependingOnEnterpriseBaseline = new(StringComparer.Ordinal);

    [Fact]
    public void SharedKernel_在所有层均不得反向依赖任何业务域()
    {
        var violations = new List<string>();

        foreach (var assembly in AllBusinessAssemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.Namespace is null || !IsInRoot(type.Namespace, "SwapMid.SharedKernel"))
                {
                    continue;
                }

                foreach (var target in GetReferencedTypes(type))
                {
                    if (target.Namespace is null || !target.Namespace.StartsWith("SwapMid.", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (IsInRoot(target.Namespace, "SwapMid.SharedKernel"))
                    {
                        continue;
                    }

                    violations.Add($"{assembly.GetName().Name}: {type.FullName} -> {target.FullName}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "SharedKernel 反向依赖了其他命名空间:\n" + string.Join("\n", violations.Distinct()));
    }

    [Fact]
    public void 对_EnterpriseManagement_的跨模块依赖必须保持为零()
    {
        var currentDomains = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assembly in AllBusinessAssemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                var sourceDomain = ExtractDomain(type.Namespace);
                if (sourceDomain is null or "EnterpriseManagement" or "SharedKernel"
                    or "Permissions" or "Localization" or "Settings" or "Data" or "MultiTenancy")
                {
                    continue;
                }

                foreach (var target in GetReferencedTypes(type))
                {
                    if (ExtractDomain(target.Namespace) == "EnterpriseManagement")
                    {
                        currentDomains.Add(sourceDomain);
                    }
                }
            }
        }

        var newlyAdded = currentDomains.Except(DomainsDependingOnEnterpriseBaseline).OrderBy(x => x).ToList();
        newlyAdded.ShouldBeEmpty(
            "发现新增业务域直接依赖 SwapMid.EnterpriseManagement（应走 SwapMid.SharedKernel.Employees 契约）: " +
            string.Join(", ", newlyAdded));
    }

    private static bool IsInRoot(string ns, string root)
        => ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);

    private static string? ExtractDomain(string? ns)
    {
        if (string.IsNullOrEmpty(ns) || !ns.StartsWith("SwapMid.", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = ns.Substring("SwapMid.".Length);
        var dot = rest.IndexOf('.');
        return dot < 0 ? rest : rest.Substring(0, dot);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static IEnumerable<Type> GetReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var raw = new List<Type?>();
        raw.Add(type.BaseType);
        raw.AddRange(type.GetInterfaces());

        foreach (var f in type.GetFields(flags)) raw.Add(f.FieldType);
        foreach (var p in type.GetProperties(flags)) raw.Add(p.PropertyType);
        foreach (var c in type.GetConstructors(flags))
            foreach (var param in c.GetParameters()) raw.Add(param.ParameterType);
        foreach (var m in type.GetMethods(flags))
        {
            raw.Add(m.ReturnType);
            foreach (var param in m.GetParameters()) raw.Add(param.ParameterType);
        }

        foreach (var t in raw)
        {
            foreach (var unwrapped in Unwrap(t))
            {
                yield return unwrapped;
            }
        }
    }

    private static IEnumerable<Type> Unwrap(Type? type)
    {
        if (type is null || type.IsGenericParameter) yield break;
        if (type.IsArray || type.IsByRef || type.IsPointer)
        {
            foreach (var inner in Unwrap(type.GetElementType())) yield return inner;
            yield break;
        }
        if (type.IsGenericType)
        {
            yield return type.GetGenericTypeDefinition();
            foreach (var arg in type.GetGenericArguments())
                foreach (var inner in Unwrap(arg)) yield return inner;
            yield break;
        }
        yield return type;
    }
}
