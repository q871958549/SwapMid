using System;
using System.Collections.Generic;
using System.Linq;

namespace SwapMid.SharedKernel.Employees;

/// <summary>
/// 员工工号对照键展开工具：兼容纯数字工号、带默认前缀（<c>SM</c>）及不同前导零宽度的工号检索。
/// </summary>
public static class EmployeeNoLookup
{
    public const string DefaultPrefix = "SM";

    /// <summary>数字段补前导零的最大宽度。</summary>
    public const int MaxDigitWidth = 8;

    public static IReadOnlyList<string> Expand(string? employeeNo)
    {
        if (string.IsNullOrWhiteSpace(employeeNo))
        {
            return [];
        }

        var raw = employeeNo.Trim();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { raw };
        if (raw.StartsWith(DefaultPrefix, StringComparison.OrdinalIgnoreCase) && raw.Length > DefaultPrefix.Length)
        {
            AddDigitVariants(keys, raw[DefaultPrefix.Length..]);
        }
        else if (raw.All(char.IsDigit))
        {
            AddDigitVariants(keys, raw);
        }

        return keys.ToList();
    }

    private static void AddDigitVariants(HashSet<string> keys, string remainder)
    {
        if (remainder.Length == 0)
        {
            return;
        }

        keys.Add(remainder);
        keys.Add(DefaultPrefix + remainder);
        if (!remainder.All(char.IsDigit))
        {
            return;
        }

        var stripped = remainder.TrimStart('0');
        if (stripped.Length == 0)
        {
            return;
        }

        keys.Add(stripped);
        keys.Add(DefaultPrefix + stripped);
        for (var width = stripped.Length + 1; width <= MaxDigitWidth; width++)
        {
            var padded = stripped.PadLeft(width, '0');
            keys.Add(padded);
            keys.Add(DefaultPrefix + padded);
        }
    }

    public static string Canonical(string? employeeNo)
    {
        if (string.IsNullOrWhiteSpace(employeeNo))
        {
            return string.Empty;
        }

        var raw = employeeNo.Trim();
        string? remainder = null;
        if (raw.StartsWith(DefaultPrefix, StringComparison.OrdinalIgnoreCase) && raw.Length > DefaultPrefix.Length)
        {
            remainder = raw[DefaultPrefix.Length..];
        }
        else if (raw.All(char.IsDigit))
        {
            remainder = raw;
        }

        if (remainder == null || remainder.Length == 0 || !remainder.All(char.IsDigit))
        {
            return raw;
        }

        var stripped = remainder.TrimStart('0');
        return stripped.Length == 0 ? raw : DefaultPrefix + stripped;
    }

    public static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string?> getNo)
        where T : class
    {
        var map = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            foreach (var key in Expand(getNo(item)))
            {
                if (ambiguous.Contains(key))
                {
                    continue;
                }

                if (map.TryGetValue(key, out var existing) && !ReferenceEquals(existing, item))
                {
                    map.Remove(key);
                    ambiguous.Add(key);
                    continue;
                }

                map.TryAdd(key, item);
            }
        }

        return map;
    }
}
