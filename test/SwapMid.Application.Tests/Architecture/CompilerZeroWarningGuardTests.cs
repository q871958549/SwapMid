using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

/// <summary>
/// 后端零编译警告硬门禁守卫（对齐前端 ESLint <c>--max-warnings=0</c>）：
/// <list type="number">
/// <item><c>common.props</c> 必须全局启用 <c>&lt;TreatWarningsAsErrors&gt;true&lt;/TreatWarningsAsErrors&gt;</c>，且禁止在全局 <c>NoWarn</c> 塞入非文档/视图生成的普通编译告警；</item>
/// <item><c>src/</c> 与 <c>test/</c> 下全部 <c>.csproj</c> 必须导入 <c>common.props</c>，禁止私自降级 <c>TreatWarningsAsErrors=false</c> 或追加项目级 <c>NoWarn</c> 逃逸。</item>
/// </list>
/// 遇到新警告请修复根因（可空守卫、参数化 SQL、非过时 API），确因上游第三方库契约受限仅允许最小单行 <c>#pragma warning disable</c> 附中文理由。
/// </summary>
public class CompilerZeroWarningGuardTests
{
    private static readonly HashSet<string> AllowedNoWarnCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "$(NoWarn)",
        "CS1591",
        "1591",
        "0436",
        "CS0436",
    };

    private static readonly HashSet<string> AllowedWarningsNotAsErrorsCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "$(WarningsNotAsErrors)",
        "NU1901",
        "NU1902",
        "NU1903",
        "NU1904",
    };

    [Fact]
    public void Common_props_must_enforce_warnings_as_errors_without_broad_suppressions()
    {
        var repoRoot = LocateRepositoryRoot();
        var commonPropsPath = Path.Combine(repoRoot, "common.props");
        File.Exists(commonPropsPath).ShouldBeTrue("仓库根缺少 common.props");

        var doc = XDocument.Load(commonPropsPath);
        var treatWarningsAsErrors = doc
            .Descendants("TreatWarningsAsErrors")
            .Select(el => el.Value.Trim())
            .ToList();

        treatWarningsAsErrors.ShouldContain(
            "true",
            "common.props 必须开启 <TreatWarningsAsErrors>true</TreatWarningsAsErrors> 防止编译警告回潮");
        treatWarningsAsErrors.ShouldNotContain(
            "false",
            "common.props 不得将 TreatWarningsAsErrors 设为 false");

        foreach (var noWarn in doc.Descendants("NoWarn"))
        {
            AssertOnlyAllowedCodes(noWarn.Value, AllowedNoWarnCodes, "common.props <NoWarn>");
        }

        foreach (var notAsErrors in doc.Descendants("WarningsNotAsErrors"))
        {
            AssertOnlyAllowedCodes(
                notAsErrors.Value,
                AllowedWarningsNotAsErrorsCodes,
                "common.props <WarningsNotAsErrors>");
        }
    }

    [Fact]
    public void All_csproj_files_must_import_common_props_and_not_opt_out_of_zero_warnings()
    {
        var repoRoot = LocateRepositoryRoot();
        var projectFiles = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "test"), "*.csproj", SearchOption.AllDirectories))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        projectFiles.Count.ShouldBeGreaterThanOrEqualTo(15);

        var violations = new List<string>();
        foreach (var csproj in projectFiles)
        {
            var rel = Path.GetRelativePath(repoRoot, csproj).Replace('\\', '/');
            var doc = XDocument.Load(csproj);

            var importsCommonProps = doc.Descendants("Import")
                .Select(el => (string?)el.Attribute("Project") ?? string.Empty)
                .Any(p => p.Replace('\\', '/').EndsWith("common.props", StringComparison.OrdinalIgnoreCase));

            if (!importsCommonProps)
            {
                violations.Add($"{rel}: 未导入 common.props，脱离统一编译门禁");
            }

            foreach (var twae in doc.Descendants("TreatWarningsAsErrors"))
            {
                if (!string.Equals(twae.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{rel}: 禁止私自覆盖 <TreatWarningsAsErrors>{twae.Value}</TreatWarningsAsErrors>");
                }
            }

            foreach (var wnae in doc.Descendants("WarningsNotAsErrors"))
            {
                var unknown = SplitCodes(wnae.Value)
                    .Where(code => !AllowedWarningsNotAsErrorsCodes.Contains(code))
                    .ToList();
                if (unknown.Count > 0)
                {
                    violations.Add($"{rel}: 禁止私自新增 <WarningsNotAsErrors> 豁免码 [{string.Join(", ", unknown)}]");
                }
            }

            foreach (var noWarn in doc.Descendants("NoWarn"))
            {
                var unknown = SplitCodes(noWarn.Value)
                    .Where(code => !AllowedNoWarnCodes.Contains(code))
                    .ToList();
                if (unknown.Count > 0)
                {
                    violations.Add($"{rel}: 禁止私自新增项目级 <NoWarn> 压制码 [{string.Join(", ", unknown)}]");
                }
            }
        }

        violations.ShouldBeEmpty(
            "所有后端与测试项目必须遵守零警告硬门禁:\n" + string.Join("\n", violations));
    }

    private static void AssertOnlyAllowedCodes(string rawValue, HashSet<string> allowlist, string location)
    {
        var unknown = SplitCodes(rawValue)
            .Where(code => !allowlist.Contains(code))
            .ToList();

        unknown.ShouldBeEmpty(
            $"{location} 存在未授权的全局豁免码 [{string.Join(", ", unknown)}]——请直接修复编译警告根因");
    }

    private static IEnumerable<string> SplitCodes(string rawValue)
        => rawValue.Split([';', ',', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string LocateRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SwapMid.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("未能从测试目录向上定位 SwapMid.slnx");
    }
}
