using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

public class ExternalAccessGuardTests
{
    private static readonly Regex RawHttpClientNew = new(
        @"\bnew\s+HttpClient\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex RawDbConnectionNew = new(
        @"\bnew\s+(SqlConnection|NpgsqlConnection|MySqlConnection)\s*\(",
        RegexOptions.Compiled);

    private static readonly HashSet<string> HttpClientAllowlist = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> DbConnectionAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "SwapMid.Application/Infrastructure/ExternalDbConnectionFactory.cs",
        "SwapMid.Application/DataIntegration/RelationalDataSourceConnection.cs",
        "SwapMid.EntityFrameworkCore/DataIntegration/NpgsqlDataPipelineExecutionFenceConnectionFactory.cs",
        "SwapMid.DbMigrator/SwapMidDemoDataSeedContributor.cs",
    };

    [Fact]
    public void Source_Code_Should_Not_Directly_New_HttpClient()
    {
        var violations = ScanSourceFiles(RawHttpClientNew, HttpClientAllowlist);

        violations.ShouldBeEmpty(
            "禁止在业务代码中直接 `new HttpClient(...)`（会导致 Socket 耗尽并绕过统一超时/重试策略）。" +
            "请在 SwapMidApplicationModule 注册具名客户端后通过 IHttpClientFactory 获取。\n违规位置:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void Source_Code_Should_Not_Directly_New_External_DbConnection()
    {
        var violations = ScanSourceFiles(RawDbConnectionNew, DbConnectionAllowlist);

        violations.ShouldBeEmpty(
            "禁止在基础设施工厂之外直接 `new SqlConnection / NpgsqlConnection / MySqlConnection`。" +
            "请统一通过 IExternalDbConnectionFactory 获取外部数据库连接。\n违规位置:\n" +
            string.Join("\n", violations));
    }

    private static List<string> ScanSourceFiles(Regex pattern, HashSet<string> allowlist)
    {
        var srcRoot = LocateSrcRoot();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(srcRoot, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase) ||
                allowlist.Contains(relative))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                if (pattern.IsMatch(lines[i]))
                {
                    violations.Add($"{relative}:{i + 1}: {trimmed}");
                }
            }
        }

        return violations;
    }

    private static string LocateSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var slnx = Path.Combine(dir.FullName, "SwapMid.slnx");
            var src = Path.Combine(dir.FullName, "src");
            if (File.Exists(slnx) && Directory.Exists(src))
            {
                return src;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("未能从测试运行目录向上定位仓库根目录（SwapMid.slnx / src）。");
    }
}
