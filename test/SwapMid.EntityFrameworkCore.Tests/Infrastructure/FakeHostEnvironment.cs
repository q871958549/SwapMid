using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace SwapMid.Infrastructure;

/// <summary>
/// <see cref="IHostEnvironment"/> 测试替身：本测试宿主不是完整 ASP.NET Core 通用主机，容器里原本
/// 没有任何 <c>IHostEnvironment</c> 注册。<c>PerformanceOaRetireeCleanupService</c> 等写外部生产系统的
/// 服务把「非 Development 环境」当作三闸之一，一旦有测试解析整个 <c>IEnumerable&lt;IScheduledJobHandler&gt;</c>
/// 集合（如 <see cref="SwapMid.Scheduling.ScheduledJobRunner"/>）就会连带构造它、缺此依赖即报错。
/// 固定 EnvironmentName=Development：与「开发环境强制 dry-run」的安全闸语义一致，保证任何测试运行都不可能
/// 被判定为「非开发环境」、不可能意外满足真写生产的环境闸。
/// </summary>
public class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "SwapMid.Tests";
    public string ContentRootPath { get; set; } = ".";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
