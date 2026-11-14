using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace SwapMid.Scheduling;

public class ScheduledJobMetadataTests
{
    [Fact]
    public void 真实处理器全部注册进目录且非空()
    {
        var types = typeof(ScheduledJobHandlerCatalog).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IScheduledJobHandler).IsAssignableFrom(t))
            .ToArray();
        var catalog = new ScheduledJobHandlerCatalog(types);
        catalog.HandlerTypes.Count.ShouldBe(types.Length);
        catalog.HandlerTypes.Count.ShouldBe(4);
    }

    [Fact]
    public void 空目录在构造时立即拒绝启动()
    {
        Should.Throw<InvalidOperationException>(() => new ScheduledJobHandlerCatalog([]));
    }

    [Fact]
    public void 解析器隔离损坏处理器且不影响健康处理器()
    {
        var provider = new RecordingProvider();
        var catalog = new ScheduledJobHandlerCatalog([typeof(BrokenHandler), typeof(HealthyHandler)]);
        var resolver = new ScheduledJobHandlerResolver(catalog, provider, NullLogger<ScheduledJobHandlerResolver>.Instance);

        var available = resolver.ResolveAvailable();
        available.Count.ShouldBe(1);
        available[0].ShouldBeOfType<HealthyHandler>();

        resolver.Find("healthy").ShouldBeOfType<HealthyHandler>();
        resolver.Find("broken").ShouldBeNull();
        resolver.Find("unknown").ShouldBeNull();
    }

    private sealed class RecordingProvider : IServiceProvider
    {
        public object? GetService(Type type)
            => type == typeof(HealthyHandler)
                ? new HealthyHandler()
                : throw new InvalidOperationException("fixture broken dependency");
    }

    private sealed class HealthyHandler : IScheduledJobHandler
    {
        public string JobKey => "healthy";
        public Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("测试不得执行作业");
    }

    private sealed class BrokenHandler : IScheduledJobHandler
    {
        public string JobKey => "broken";
        public Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("测试不得执行作业");
    }
}
