using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace SwapMid.Permissions;

/// <summary>
/// 防复发:<c>SwapMidPermissionDefinitionProvider</c> 里全部 <c>L("Permission:...")</c> 调用的键，
/// 必须在 zh-Hans.json 有对应翻译——否则 ABP 本地化找不到资源时回退显示原始 key
/// （2026-07-20 曾漏 <c>Permission:ProjectManagement:Warehouses</c> 一键实证，见
/// docs/account-roles-permissions-blueprint.md §2.1 P0-1/P0-2）。
///
/// 采用蓝图预案的「退路」而非首选的 DI 容器解析 <c>IPermissionDefinitionManager</c>/
/// <c>IStringLocalizerFactory</c>：本仓 Application/Domain 模块刻意不依赖 EntityFrameworkCore
/// （纯 DDD 分层，见 src/CLAUDE.md 分层规范），<c>SwapMidTestBaseModule</c> 在模块启动时
/// 会同步跑 <c>IDataSeeder.SeedAsync()</c>（内置 PermissionDataSeedContributor 等需要真实仓储），
/// Application.Tests 单独跑时没有任何 EF Core / InMemory 仓储 Provider 注册，直接 boot
/// <c>SwapMidApplicationTestModule</c> 会在数据种子阶段抛异常，DI 路线在本项目走不通
/// （本仓现存的 DI 集成测试均以 <c>&lt;TStartupModule&gt;</c> 抽象基类形式只在 EntityFrameworkCore.Tests
/// 具体化，如 CodeRuleEntityTypeProviderTests / SampleAppServiceTests / SampleDomainTests）。
/// 改走源码正则扫 L("...") 键、对 zh-Hans.json 的 texts 做差集断言：纯文件级比对，
/// 不连库、不启模块，精确复现同一类回归，且能在 Application.Tests 单独跑时红绿分明。
///
/// 只锁 zh-Hans；en/ja 权限翻译是已知存量缺口（归 docs/i18n-blueprint.md），勿在此顺带断言。
/// </summary>
public class PermissionLocalizationKeysTests
{
    private static readonly Regex LCallPattern = new(@"L\(""([^""]+)""\)", RegexOptions.Compiled);

    [Fact]
    public void Provider全部L调用的键_在zh_Hans_json里都必须存在()
    {
        var providerDir = Path.GetDirectoryName(GetRepoPath(
            "src/SwapMid.Application.Contracts/Permissions/SwapMidPermissionDefinitionProvider.cs"))!;
        var zhHansPath = GetRepoPath(
            "src/SwapMid.Domain.Shared/Localization/SwapMid/zh-Hans.json");

        Directory.Exists(providerDir).ShouldBeTrue($"权限定义目录未找到:{providerDir}");
        File.Exists(zhHansPath).ShouldBeTrue($"zh-Hans.json 未找到,检查仓库根路径推导是否失效:{zhHansPath}");

        var providerFiles = Directory.GetFiles(providerDir, "SwapMidPermissionDefinitionProvider*.cs");
        providerFiles.Length.ShouldBeGreaterThanOrEqualTo(1, "权限定义文件未找到");
        var providerSource = string.Join(
            "\n",
            providerFiles.OrderBy(path => path, StringComparer.Ordinal).Select(File.ReadAllText));
        var usedKeys = LCallPattern.Matches(providerSource)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        // 正则本身失效(比如 Provider 改了 L() 的调用写法)不该被误判成"零缺失"而静默通过。
        usedKeys.Count.ShouldBeGreaterThan(30, "解析出的 L(\"...\") 键数量异常偏少,正则可能已跟不上 Provider 源码写法");

        using var doc = JsonDocument.Parse(File.ReadAllText(zhHansPath));
        var textsElement = doc.RootElement.TryGetProperty("texts", out var lowerTexts)
            ? lowerTexts
            : doc.RootElement.GetProperty("Texts");
        var translatedKeys = textsElement
            .EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet();

        var missingKeys = usedKeys.Where(k => !translatedKeys.Contains(k)).OrderBy(k => k).ToList();

        missingKeys.ShouldBeEmpty(
            $"以下权限 L() 键在 zh-Hans.json 缺翻译,ABP 本地化会回退显示原始 key:{string.Join(", ", missingKeys)}");
    }

    /// <summary>用 CallerFilePath 拿到本文件在编译时的绝对路径,反推仓库根目录——纯文件系统比对,不依赖 DI/工作目录。</summary>
    private static string GetRepoPath(string relativeFromRepoRoot, [CallerFilePath] string here = "")
    {
        // here = {repoRoot}/test/SwapMid.Application.Tests/Permissions/xxx.cs
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        return Path.Combine(repoRoot, relativeFromRepoRoot.Replace('/', Path.DirectorySeparatorChar));
    }
}
