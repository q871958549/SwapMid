using System;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Infrastructure;
using Shouldly;
using Xunit;

namespace SwapMid.InfrastructureTests;

public class ExternalDataSourceConcurrencyGateTests
{
    [Fact]
    public async Task 容量耗尽应立即拒绝且释放后可再次取得()
    {
        var gate = new ExternalDataSourceConcurrencyGate();
        var first = await gate.TryAcquireAsync(
            "OAQuery:EmployeeSearch",
            1,
            CancellationToken.None);

        first.ShouldNotBeNull();
        (await gate.TryAcquireAsync(
                "OAQuery:EmployeeSearch",
                1,
                CancellationToken.None))
            .ShouldBeNull();

        await first!.DisposeAsync();
        var next = await gate.TryAcquireAsync(
            "OAQuery:EmployeeSearch",
            1,
            CancellationToken.None);
        next.ShouldNotBeNull();
        await next!.DisposeAsync();
    }

    [Fact]
    public async Task 同一key运行期容量不一致应拒绝启动式漂移()
    {
        var gate = new ExternalDataSourceConcurrencyGate();
        var lease = await gate.TryAcquireAsync(
            "OAQuery:EmployeeSearch",
            1,
            CancellationToken.None);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await gate.TryAcquireAsync(
                "OAQuery:EmployeeSearch",
                2,
                CancellationToken.None));

        await lease!.DisposeAsync();
    }

    [Fact]
    public async Task 已取消调用不得占用许可()
    {
        var gate = new ExternalDataSourceConcurrencyGate();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await gate.TryAcquireAsync(
                "OAQuery:EmployeeSearch",
                1,
                cancellation.Token));

        var lease = await gate.TryAcquireAsync(
            "OAQuery:EmployeeSearch",
            1,
            CancellationToken.None);
        lease.ShouldNotBeNull();
        await lease!.DisposeAsync();
    }

    [Fact]
    public async Task 许可重复释放必须幂等且不得突破容量()
    {
        var gate = new ExternalDataSourceConcurrencyGate();
        var lease = await gate.TryAcquireAsync(
            "OAQuery:EmployeeSearch",
            1,
            CancellationToken.None);

        lease.ShouldNotBeNull();
        await lease!.DisposeAsync();
        await lease.DisposeAsync();

        var reacquired = await gate.TryAcquireAsync(
            "OAQuery:EmployeeSearch",
            1,
            CancellationToken.None);
        reacquired.ShouldNotBeNull();
        (await gate.TryAcquireAsync(
                "OAQuery:EmployeeSearch",
                1,
                CancellationToken.None))
            .ShouldBeNull();
        await reacquired!.DisposeAsync();
    }
}
