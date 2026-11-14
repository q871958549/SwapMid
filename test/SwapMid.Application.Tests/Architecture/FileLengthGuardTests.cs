using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

public class FileLengthGuardTests
{
    private const int HardLineLimit = 500;

    private static readonly HashSet<string> BaselineOverLimit = new(StringComparer.OrdinalIgnoreCase)
    {
        "SwapMid.Application/DataIntegration/EtlTargetAtomicWriter.cs",
        "SwapMid.Application/DataIntegration/EtlTargetPreflight.cs",
        "SwapMid.Application/DataIntegration/EtlTargetReceiptStore.cs",
        "SwapMid.HttpApi.Host/SwapMidHttpApiHostModule.cs"
    };

    [Fact]
    public void Source_Files_Should_Not_Exceed_500_Lines()
    {
        var srcRoot = LocateSrcRoot();
        var newlyOverLimit = new List<string>();
        var currentOverLimit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(srcRoot, file).Replace('\\', '/');
            if (IsExcluded(relative))
            {
                continue;
            }

            var lineCount = File.ReadLines(file).Count();
            if (lineCount <= HardLineLimit)
            {
                continue;
            }

            currentOverLimit.Add(relative);
            if (!BaselineOverLimit.Contains(relative))
            {
                newlyOverLimit.Add($"{relative} ({lineCount} 行 > 上限 {HardLineLimit} 行)");
            }
        }

        newlyOverLimit.ShouldBeEmpty(
            "发现新的超长源码文件（单文件上限 500 行，请按职责拆分后再提交）:\n" +
            string.Join("\n", newlyOverLimit));

        var staleBaseline = BaselineOverLimit.Except(currentOverLimit, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        staleBaseline.ShouldBeEmpty(
            "恭喜！以下文件已降至 500 行以内，请同步从 FileLengthGuardTests.BaselineOverLimit 移除以收紧门禁:\n" +
            string.Join("\n", staleBaseline));
    }

    private static bool IsExcluded(string relativePath)
    {
        var segments = relativePath.Split('/');
        if (segments.Any(s =>
                s.Equals("Migrations", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("bin", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return relativePath.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase);
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

        throw new InvalidOperationException("未能从测试目录向上定位 SwapMid.slnx / src。");
    }
}
