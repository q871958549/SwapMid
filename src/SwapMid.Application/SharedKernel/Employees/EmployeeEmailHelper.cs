using System.Linq;
using ToolGood.Words;

namespace SwapMid.SharedKernel.Employees;

/// <summary>员工邮箱默认值生成。</summary>
public static class EmployeeEmailHelper
{
    public const string DefaultEmailDomain = "example.com";

    /// <summary>姓名全拼小写；去掉声调、空格和非字母数字，缺省时回退 user。</summary>
    public static string BuildDefaultEmail(string? name)
    {
        var pinyin = WordsHelper.GetPinyin(name ?? string.Empty, "");
        var local = new string(pinyin
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        if (string.IsNullOrWhiteSpace(local))
        {
            local = "user";
        }

        return $"{local}@{DefaultEmailDomain}";
    }

    /// <summary>有 OA 邮箱时使用原值，否则按姓名全拼生成默认邮箱。</summary>
    public static string ResolveEmail(string? email, string? name)
        => string.IsNullOrWhiteSpace(email) ? BuildDefaultEmail(name) : email.Trim();
}
