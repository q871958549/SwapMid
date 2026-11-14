using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Storage;
using SwapMid.HttpApiHost.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;
using Xunit;

namespace SwapMid.Scheduling;

public class JobSchedulingMetadataTests
{
    [Fact]
    public async Task 列表合并处理器参数与互斥组且不构造执行管道()
    {
        var storage = Substitute.For<JobStorage>();
        var connection = Substitute.For<IStorageConnection>();
        connection.GetAllItemsFromSet("recurring-jobs").Returns(new HashSet<string>());
        storage.GetConnection().Returns(connection);

        var resolver = Substitute.For<IScheduledJobHandlerResolver>();
        resolver.ResolveAvailable().Returns([new FixtureHandler()]);

        var settings = Substitute.For<ISettingProvider>();
        using var services = new ServiceCollection()
            .AddSingleton(settings)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

        var service = new JobSchedulingAppService(
            null!,
            storage,
            null!,
            resolver,
            null!,
            null!,
            null!,
            null!)
        {
            LazyServiceProvider = new AbpLazyServiceProvider(services),
        };

        var result = await service.GetListAsync();
        var job = result.Single(x => x.Key == ScheduledJobKeys.AuditLogCleanup);
        job.SupportsParameterizedRun.ShouldBeTrue();
        job.Parameters.Single().Name.ShouldBe("fixture");
        job.MutexGroup.ShouldBe("fixture-mutex");
        result.Count.ShouldBe(ScheduledJobCatalog.All.Count);
    }

    private sealed class FixtureHandler : IScheduledJobHandler
    {
        public string JobKey => ScheduledJobKeys.AuditLogCleanup;
        public IReadOnlyList<JobParameterSchema> Parameters => [new JobParameterSchema { Name = "fixture" }];
        public string? MutexGroup => "fixture-mutex";
        public Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("测试不得执行任务");
    }
}
