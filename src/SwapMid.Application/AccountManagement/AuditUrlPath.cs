using System;
using System.Text;

namespace SwapMid.AccountManagement;

/// <summary>
/// 复刻 ASP.NET Core <c>PathString.ToUriComponent()</c> 的路径编码，用于把「解码后的原文路径」
/// 编码成与审计落库形态一致的字符串——ABP 审计贡献者(AspNetCoreAuditLogContributor)存的
/// <c>AuditLog.Url</c> 取自 <c>Request.Path.ToString()</c>，即 PathString 的百分号编码形态；
/// 查询侧(<see cref="AccountRoleAppService"/> 的权限变更历史)必须用同一编码构串，
/// 否则含中文等非 ASCII 字符的角色名 Contains 永不命中。
/// ⚠️ 不直接用 <c>Microsoft.AspNetCore.Http.PathString</c> 的原因：Application 层是纯类库
/// (无 FrameworkReference,经查 project.assets.json 依赖闭包中无任何 Microsoft.AspNetCore.Http.* 程序集)，
/// 该类型在本层不可见；故按 aspnetcore 源码(Http.Abstractions/src/{PathString,PathStringHelper}.cs)
/// 复刻同一算法：RFC 3986 路径合法字符(unreserved/sub-delims/':'/'@'/'/')原样保留，
/// 已是 %XX 形态的序列原样保留，其余字符段交 <see cref="Uri.EscapeDataString"/>(UTF-8 逐字节大写 %XX)。
/// </summary>
internal static class AuditUrlPath
{
    /// <summary>把解码原文路径编码为 PathString.ToUriComponent() 的等价形态(全程合法则原串直返)。</summary>
    public static string ToUriComponent(string path)
    {
        StringBuilder? buffer = null;
        var start = 0;
        var count = 0;
        var requiresEscaping = false;
        var i = 0;

        while (i < path.Length)
        {
            var isPercentEncodedChar = false;
            if (IsValidPathChar(path[i]) || (isPercentEncodedChar = IsPercentEncodedChar(path, i)))
            {
                if (requiresEscaping)
                {
                    // 刚结束一段非法字符区,整段转义后落入缓冲
                    buffer ??= new StringBuilder(path.Length * 3);
                    buffer.Append(Uri.EscapeDataString(path.Substring(start, count)));
                    requiresEscaping = false;
                    start = i;
                    count = 0;
                }

                if (isPercentEncodedChar)
                {
                    count += 3;
                    i += 3;
                }
                else
                {
                    count++;
                    i++;
                }
            }
            else
            {
                if (!requiresEscaping)
                {
                    // 刚结束一段合法字符区,原样落入缓冲
                    buffer ??= new StringBuilder(path.Length * 3);
                    buffer.Append(path, start, count);
                    requiresEscaping = true;
                    start = i;
                    count = 0;
                }

                count++;
                i++;
            }
        }

        if (buffer == null)
        {
            // 全程未遇非法字符,无需任何转义
            return path;
        }

        if (count > 0)
        {
            if (requiresEscaping)
            {
                buffer.Append(Uri.EscapeDataString(path.Substring(start, count)));
            }
            else
            {
                buffer.Append(path, start, count);
            }
        }

        return buffer.ToString();
    }

    // RFC 3986 路径合法字符:ALPHA / DIGIT / unreserved 其余("-" "." "_" "~") /
    // sub-delims("!" "$" "&" "'" "(" ")" "*" "+" "," ";" "=") / ":" / "@" / "/"。
    private static bool IsValidPathChar(char c)
    {
        if (c > 127)
        {
            return false;
        }

        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
        {
            return true;
        }

        return "-._~!$&'()*+,;=:@/".IndexOf(c) >= 0;
    }

    // "%" 后跟两位十六进制视为已转义序列,原样保留(与 PathStringHelper.IsPercentEncodedChar 同判定)。
    private static bool IsPercentEncodedChar(string str, int index)
        => index < str.Length - 2
           && str[index] == '%'
           && IsHexadecimalChar(str[index + 1])
           && IsHexadecimalChar(str[index + 2]);

    private static bool IsHexadecimalChar(char c)
        => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
}
