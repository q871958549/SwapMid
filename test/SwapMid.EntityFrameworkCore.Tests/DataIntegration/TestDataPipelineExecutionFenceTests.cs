using System;
using System.Threading.Tasks;
using NSubstitute;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace SwapMid.DataIntegration;

public sealed class TestDataPipelineExecutionFenceTests
{
    [Fact]
    public async Task 进程内替身必须同时锁定双目标并在释放后允许重取()
    {
        var currentTenant = Substitute.For<ICurrentTenant>();
        var fence = new TestDataPipelineExecutionFence(currentTenant);
        var targets = new[] { "legacy-target", "verified-target" };

        var first = await fence.TryAcquireAsync(
            Guid.NewGuid(),
            targets,
            capacitySlots: 0);
        first.Rejection.ShouldBe(DataPipelineExecutionFenceRejection.None);
        first.Handle.ShouldNotBeNull();

        var conflict = await fence.TryAcquireAsync(
            Guid.NewGuid(),
            new[] { targets[1] },
            capacitySlots: 0);
        conflict.Rejection.ShouldBe(
            DataPipelineExecutionFenceRejection.ResourceBusy);
        conflict.Handle.ShouldBeNull();

        await first.Handle!.DisposeAsync();

        await using var reacquired = (await fence.TryAcquireAsync(
            Guid.NewGuid(),
            targets,
            capacitySlots: 0)).Handle;
        reacquired.ShouldNotBeNull();
    }

    [Fact]
    public async Task 进程内替身双目标部分获取失败后不得泄漏已取得键()
    {
        var currentTenant = Substitute.For<ICurrentTenant>();
        var fence = new TestDataPipelineExecutionFence(currentTenant);
        await using var blocked = (await fence.TryAcquireAsync(
            Guid.NewGuid(),
            new[] { "verified-target" },
            capacitySlots: 0)).Handle;
        blocked.ShouldNotBeNull();

        var rejected = await fence.TryAcquireAsync(
            Guid.NewGuid(),
            new[] { "legacy-target", "verified-target" },
            capacitySlots: 0);
        rejected.Rejection.ShouldBe(
            DataPipelineExecutionFenceRejection.ResourceBusy);

        await using var legacy = (await fence.TryAcquireAsync(
            Guid.NewGuid(),
            new[] { "legacy-target" },
            capacitySlots: 0)).Handle;
        legacy.ShouldNotBeNull(
            "双目标尝试失败时必须逆序释放已取得的 legacy 键与 pipeline 键");
    }
}
