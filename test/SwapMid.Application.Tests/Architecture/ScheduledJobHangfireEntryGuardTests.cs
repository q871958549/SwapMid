using System;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

public class ScheduledJobHangfireEntryGuardTests
{
    [Fact]
    public void Runner驱动的Hangfire入口必须直返抛错Task_控制页保持兼容结果语义()
    {
        var root = FindRepositoryRoot();
        var applicationRoot = Path.Combine(
            root,
            "src",
            "SwapMid.Application");
        var scheduling = Path.Combine(
            applicationRoot,
            "Scheduling");

        var runnerJobs = Directory
            .EnumerateFiles(applicationRoot, "*Job.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                File.ReadAllText(path).Contains("IRunnerDrivenJob,", StringComparison.Ordinal))
            .ToList();

        runnerJobs.Count.ShouldBe(4);
        foreach (var path in runnerJobs)
        {
            var source = File.ReadAllText(path);
            source.ShouldContain(
                "ExecuteAsync(System.Threading.CancellationToken cancellationToken)");
            source.ShouldContain("_runner.RunForHangfireAsync(");
            source.ShouldContain("cancellationToken);");
            source.ShouldNotContain("_runner.RunAsync(");
            source.ShouldNotContain("async Task ExecuteAsync");
            source.ShouldNotContain("await _runner.RunForHangfireAsync(");
        }

        var legacyBackgroundSource = File.ReadAllText(Path.Combine(
            scheduling,
            "RunScheduledJobBackgroundJob.cs"));
        legacyBackgroundSource.ShouldContain("RunScheduledJobArgs : IRunnerDrivenJobArgs");
        legacyBackgroundSource.ShouldContain("旧版 ABP 后台承载");
        legacyBackgroundSource.ShouldContain("不得恢复新入队");

        var directBackgroundSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.HttpApi.Host",
            "Scheduling",
            "RunScheduledJobHangfireJob.cs"));
        directBackgroundSource.ShouldContain(
            "RunScheduledJobHangfireJob : IRunnerDrivenJob");
        directBackgroundSource.ShouldContain(
            "public Task ExecuteAsync(");
        directBackgroundSource.ShouldContain(
            "_runner.RunForHangfireAsync(");
        directBackgroundSource.ShouldNotContain(
            "async Task ExecuteAsync");
        directBackgroundSource.ShouldNotContain(
            "await _runner.RunForHangfireAsync(");

        var controlPageSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.HttpApi.Host",
            "Scheduling",
            "JobSchedulingAppService.cs"));
        controlPageSource.ShouldContain("_runner.RunAsync(");
        controlPageSource.ShouldContain("_cancellationTokenProvider.Token");
        controlPageSource.ShouldNotContain("_runner.RunForHangfireAsync(");
        controlPageSource.ShouldContain(
            "_backgroundJobClient.Enqueue<RunScheduledJobHangfireJob>(");
        controlPageSource.ShouldNotContain(
            "_backgroundJobManager.EnqueueAsync(");

        var catalogSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.HttpApi.Host",
            "Scheduling",
            "ScheduledJobCatalog.cs"));
        CountOccurrences(
            catalogSource,
            "job.ExecuteAsync(System.Threading.CancellationToken.None)").ShouldBe(4);
    }

    [Fact]
    public void 平台内置清理任务必须显式标记IsDestructive并审慎声明IsLongRunning()
    {
        var catalogSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SwapMid.HttpApi.Host",
            "Scheduling",
            "ScheduledJobCatalog.cs"));

        CountOccurrences(catalogSource, "IsDestructive = true").ShouldBe(4);
        CountOccurrences(catalogSource, "IsLongRunning = true").ShouldBe(0);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "SwapMid.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("未找到仓库根目录 SwapMid.slnx");
    }

    private static int CountOccurrences(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;
}
