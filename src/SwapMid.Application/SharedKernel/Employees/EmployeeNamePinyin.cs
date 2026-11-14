using System;
using System.Collections.Generic;
using System.Linq;
using ToolGood.Words;

namespace SwapMid.SharedKernel.Employees;

/// <summary>
/// 员工姓名拼音首字母（与前端 <c>pinyinInitials</c> 同口径：<c>ty</c> 命中「田烨」）。
/// 供员工列表 <c>Filter</c> 在纯字母查询时补 SQL <c>Contains</c> 覆盖不到的读音匹配。
/// </summary>
public static class EmployeeNamePinyin
{
    /// <summary>1–16 位纯拉丁字母，视为拼音首字母查询，而不是中文名/工号/手机号。</summary>
    public static bool LooksLikeInitialsQuery(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return false;
        }

        var text = filter.Trim();
        return text.Length is >= 1 and <= 16 && text.All(char.IsAsciiLetter);
    }

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !name.Any(IsCjk))
        {
            return string.Empty;
        }

        return WordsHelper.GetFirstPinyin(name).ToLowerInvariant();
    }

    public static bool InitialsContain(string? name, string query)
    {
        var needle = query.Trim().ToLowerInvariant();
        return needle.Length > 0 && Initials(name).Contains(needle, StringComparison.Ordinal);
    }

    /// <summary>从已投影的 Id+姓名里挑出拼音首字母命中项；非首字母查询返回空列表。</summary>
    public static List<Guid> MatchIds(IEnumerable<(Guid Id, string? Name)> items, string query)
    {
        if (!LooksLikeInitialsQuery(query))
        {
            return [];
        }

        return items
            .Where(item => InitialsContain(item.Name, query))
            .Select(item => item.Id)
            .ToList();
    }

    private static bool IsCjk(char c) => c is >= '\u4e00' and <= '\u9fff';
}
