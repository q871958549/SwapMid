using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Infrastructure;
using NSubstitute;
using Shouldly;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;
using Xunit;

namespace SwapMid.SystemManagement;

public class SystemHealthConcurrencyTests
{
    [Fact]
    public void 外部依赖检查不应混入平台自身就绪探针()
    {
        var rabbitMq = HealthCheckCatalog.Items.Single(
            item => item.Name == "RabbitMQ 消息代理");

        rabbitMq.PromQl.ShouldBe("up{job=~\".*rabbitmq.*\"}");
    }

    [Fact]
    public async Task 公共体检入口应并发查询全部项目且保持清单顺序()
    {
        var prometheus = new CoordinatedPrometheusClient(HealthCheckCatalog.Items.Count);
        var checkedAt = new DateTime(2026, 7, 22, 9, 30, 0, DateTimeKind.Local);
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(checkedAt);
        var lazyServiceProvider = Substitute.For<IAbpLazyServiceProvider>();
        lazyServiceProvider.LazyGetRequiredService<IClock>().Returns(clock);
        lazyServiceProvider.LazyGetRequiredService(typeof(IClock)).Returns(clock);
        var service = new SystemHealthAppService(prometheus)
        {
            LazyServiceProvider = lazyServiceProvider,
        };

        var evaluation = service.GetAsync();

        try
        {
            await prometheus.AllCallsStarted.WaitAsync(TimeSpan.FromSeconds(5));
            prometheus.StartedCallCount.ShouldBe(HealthCheckCatalog.Items.Count);
            prometheus.MaxConcurrentCallCount.ShouldBe(HealthCheckCatalog.Items.Count);
        }
        finally
        {
            prometheus.Release();
        }

        var result = await evaluation;
        result.CheckedAt.ShouldBe(checkedAt);
        result.Items.Select(item => item.Name)
            .ShouldBe(HealthCheckCatalog.Items.Select(item => item.Name));
    }

    private sealed class CoordinatedPrometheusClient : IPrometheusClient
    {
        private readonly int _expectedCallCount;
        private readonly TaskCompletionSource<bool> _allCallsStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startedCallCount;
        private int _activeCallCount;
        private int _maxConcurrentCallCount;

        public CoordinatedPrometheusClient(int expectedCallCount)
        {
            _expectedCallCount = expectedCallCount;
        }

        public bool IsConfigured => true;
        public Task AllCallsStarted => _allCallsStarted.Task;
        public int StartedCallCount => Volatile.Read(ref _startedCallCount);
        public int MaxConcurrentCallCount => Volatile.Read(ref _maxConcurrentCallCount);

        public async Task<double?> QueryScalarAsync(string promql)
        {
            await WaitForReleaseAsync();
            return 1d;
        }

        public async Task<IReadOnlyList<PrometheusSample>> QueryVectorAsync(string promql)
        {
            await WaitForReleaseAsync();
            return new[] { new PrometheusSample(1d, new Dictionary<string, string>()) };
        }

        public void Release() => _release.TrySetResult(true);

        private async Task WaitForReleaseAsync()
        {
            var active = Interlocked.Increment(ref _activeCallCount);
            UpdateMaximum(active);
            if (Interlocked.Increment(ref _startedCallCount) == _expectedCallCount)
            {
                _allCallsStarted.TrySetResult(true);
            }

            try
            {
                await _release.Task;
            }
            finally
            {
                Interlocked.Decrement(ref _activeCallCount);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            var current = Volatile.Read(ref _maxConcurrentCallCount);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(ref _maxConcurrentCallCount, candidate, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
