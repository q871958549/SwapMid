using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Testcontainers.PostgreSql;
using Volo.Abp.Data;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>
/// 进程内 fake 无法证明 PostgreSQL session advisory lock 的真实互斥与释放语义；
/// 此契约使用独立非池化物理连接覆盖管道、目标资源集合及全局容量三层执行权。
/// </summary>
public sealed class NpgsqlDataPipelineExecutionFencePostgreSqlContractTests
{
    private const string PostgreSqlImage =
        "postgres:18.4@sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a";

    [PostgreSqlContractFact]
    [Trait("Category", "PostgreSqlContract")]
    public async Task 真实PostgreSql必须执行管道目标容量互斥并在释放后允许重取()
    {
        await using var postgres = new PostgreSqlBuilder(PostgreSqlImage)
            .WithDatabase("swapmid_etl_fence_contract")
            .WithUsername("swapmid_contract")
            .WithPassword("SwapMidContractOnly_2026")
            .Build();
        await postgres.StartAsync();

        var fence = CreateFence(postgres.GetConnectionString());

        await AssertSamePipelineConflictAsync(fence);
        await AssertSameTargetConflictAsync(fence);
        await AssertDualTargetConflictAndRollbackAsync(fence);
        await AssertDifferentTargetsWithinCapacityAsync(fence);
        await AssertDualTargetsConsumeSingleCapacitySlotAsync(fence);
        await AssertCapacityExhaustedAsync(fence);
        await AssertDisposeAllowsReacquisitionAsync(fence);
    }

    private static async Task AssertSamePipelineConflictAsync(
        IDataPipelineExecutionFence fence)
    {
        var pipelineId = Guid.NewGuid();
        await using var held = await AcquireAsync(
            fence,
            pipelineId,
            "contract:same-pipeline:first-target",
            capacitySlots: 0);

        await AssertRejectedAsync(
            fence,
            pipelineId,
            "contract:same-pipeline:second-target",
            capacitySlots: 0,
            DataPipelineExecutionFenceRejection.ResourceBusy);
    }

    private static async Task AssertSameTargetConflictAsync(
        IDataPipelineExecutionFence fence)
    {
        const string target = "contract:same-target";
        await using var held = await AcquireAsync(
            fence,
            Guid.NewGuid(),
            target,
            capacitySlots: 0);

        await AssertRejectedAsync(
            fence,
            Guid.NewGuid(),
            target,
            capacitySlots: 0,
            DataPipelineExecutionFenceRejection.ResourceBusy);
    }

    private static async Task AssertDualTargetConflictAndRollbackAsync(
        IDataPipelineExecutionFence fence)
    {
        var targets = new[]
        {
            "contract:dual-target:legacy",
            "contract:dual-target:verified",
        };

        await using (var held = await AcquireAsync(
                         fence,
                         Guid.NewGuid(),
                         targets,
                         capacitySlots: 0))
        {
            foreach (var target in targets)
            {
                await AssertRejectedAsync(
                    fence,
                    Guid.NewGuid(),
                    new[] { target },
                    capacitySlots: 0,
                    DataPipelineExecutionFenceRejection.ResourceBusy);
            }

            await AssertRejectedAsync(
                fence,
                Guid.NewGuid(),
                new[] { targets[1], targets[0] },
                capacitySlots: 0,
                DataPipelineExecutionFenceRejection.ResourceBusy);
        }

        // 两种阻塞排列都验证一遍：无论被占用键在稳定排序中的前后位置，
        // 双目标获取失败后都不得遗留另一个已取得的 session lock。
        foreach (var blockedTarget in targets)
        {
            var otherTarget = blockedTarget == targets[0]
                ? targets[1]
                : targets[0];
            await using var blocked = await AcquireAsync(
                fence,
                Guid.NewGuid(),
                blockedTarget,
                capacitySlots: 0);

            await AssertRejectedAsync(
                fence,
                Guid.NewGuid(),
                targets,
                capacitySlots: 0,
                DataPipelineExecutionFenceRejection.ResourceBusy);

            await using var other = await AcquireAsync(
                fence,
                Guid.NewGuid(),
                otherTarget,
                capacitySlots: 0);
        }
    }

    private static async Task AssertDifferentTargetsWithinCapacityAsync(
        IDataPipelineExecutionFence fence)
    {
        await using var first = await AcquireAsync(
            fence,
            Guid.NewGuid(),
            "contract:capacity-allowed:first",
            capacitySlots: 2);
        await using var second = await AcquireAsync(
            fence,
            Guid.NewGuid(),
            "contract:capacity-allowed:second",
            capacitySlots: 2);
    }

    private static async Task AssertDualTargetsConsumeSingleCapacitySlotAsync(
        IDataPipelineExecutionFence fence)
    {
        await using var first = await AcquireAsync(
            fence,
            Guid.NewGuid(),
            new[]
            {
                "contract:dual-capacity:first-legacy",
                "contract:dual-capacity:first-verified",
            },
            capacitySlots: 2);
        await using var second = await AcquireAsync(
            fence,
            Guid.NewGuid(),
            "contract:dual-capacity:second",
            capacitySlots: 2);

        await AssertRejectedAsync(
            fence,
            Guid.NewGuid(),
            "contract:dual-capacity:third",
            capacitySlots: 2,
            DataPipelineExecutionFenceRejection.CapacityExhausted);
    }

    private static async Task AssertCapacityExhaustedAsync(
        IDataPipelineExecutionFence fence)
    {
        await using var first = await AcquireAsync(
            fence,
            Guid.NewGuid(),
            "contract:capacity-full:first",
            capacitySlots: 2);
        await using var second = await AcquireAsync(
            fence,
            Guid.NewGuid(),
            "contract:capacity-full:second",
            capacitySlots: 2);

        await AssertRejectedAsync(
            fence,
            Guid.NewGuid(),
            "contract:capacity-full:third",
            capacitySlots: 2,
            DataPipelineExecutionFenceRejection.CapacityExhausted);
    }

    private static async Task AssertDisposeAllowsReacquisitionAsync(
        IDataPipelineExecutionFence fence)
    {
        var pipelineId = Guid.NewGuid();
        const string target = "contract:dispose-reacquire";
        var first = await AcquireAsync(
            fence,
            pipelineId,
            target,
            capacitySlots: 1);
        await first.DisposeAsync();

        await using var reacquired = await AcquireAsync(
            fence,
            pipelineId,
            target,
            capacitySlots: 1);
    }

    private static async Task<IDataPipelineExecutionFenceHandle> AcquireAsync(
        IDataPipelineExecutionFence fence,
        Guid pipelineId,
        string targetResource,
        int capacitySlots)
        => await AcquireAsync(
            fence,
            pipelineId,
            new[] { targetResource },
            capacitySlots);

    private static async Task<IDataPipelineExecutionFenceHandle> AcquireAsync(
        IDataPipelineExecutionFence fence,
        Guid pipelineId,
        IReadOnlyCollection<string> targetResources,
        int capacitySlots)
    {
        var result = await fence.TryAcquireAsync(
            pipelineId,
            targetResources,
            capacitySlots);

        result.Rejection.ShouldBe(DataPipelineExecutionFenceRejection.None);
        result.Handle.ShouldNotBeNull();
        return result.Handle!;
    }

    private static async Task AssertRejectedAsync(
        IDataPipelineExecutionFence fence,
        Guid pipelineId,
        string targetResource,
        int capacitySlots,
        DataPipelineExecutionFenceRejection expectedRejection)
        => await AssertRejectedAsync(
            fence,
            pipelineId,
            new[] { targetResource },
            capacitySlots,
            expectedRejection);

    private static async Task AssertRejectedAsync(
        IDataPipelineExecutionFence fence,
        Guid pipelineId,
        IReadOnlyCollection<string> targetResources,
        int capacitySlots,
        DataPipelineExecutionFenceRejection expectedRejection)
    {
        var result = await fence.TryAcquireAsync(
            pipelineId,
            targetResources,
            capacitySlots);

        result.Rejection.ShouldBe(expectedRejection);
        result.Handle.ShouldBeNull();
    }

    private static NpgsqlDataPipelineExecutionFence CreateFence(
        string connectionString)
    {
        var resolver = Substitute.For<IConnectionStringResolver>();
        resolver.ResolveAsync(ConnectionStrings.DefaultConnectionStringName)
            .Returns(Task.FromResult(connectionString));
        var factory = new NpgsqlDataPipelineExecutionFenceConnectionFactory(
            resolver);
        return new NpgsqlDataPipelineExecutionFence(
            factory,
            NullLogger<NpgsqlDataPipelineExecutionFence>.Instance);
    }
}
