using System;
using System.IO;
using System.Linq;

namespace SwapMid.Architecture;

/// <summary>
/// 宿主模块按关注点拆成了多个 partial（<c>SwapMidHttpApiHostModule*.cs</c>）。
/// 源码级守卫一律经此读取全部 partial 的拼接内容——只读主文件会让断言随代码挪到 partial 而误红或误绿。
/// </summary>
internal static class HostModuleSource
{
    public static string ReadAll()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SwapMid.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new DirectoryNotFoundException("未能定位 SwapMid.slnx");
        }

        var hostDir = Path.Combine(dir.FullName, "src", "SwapMid.HttpApi.Host");
        var files = Directory.GetFiles(hostDir, "SwapMidHttpApiHostModule*.cs")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            throw new FileNotFoundException("未找到宿主模块源码", hostDir);
        }

        return string.Join("\n", files.Select(File.ReadAllText));
    }
}
