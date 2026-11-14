using System;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace SwapMid.DataIntegration;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class PipelineExecutionRepositoryTests
    : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IRepository<DataSource, Guid> _sourceRepository;
    private readonly IDataPipelineRepository _pipelineRepository;
    private readonly IPipelineExecutionRepository _executionRepository;

    public PipelineExecutionRepositoryTests()
    {
        _sourceRepository = GetRequiredService<IRepository<DataSource, Guid>>();
        _pipelineRepository = GetRequiredService<IDataPipelineRepository>();
        _executionRepository = GetRequiredService<IPipelineExecutionRepository>();
    }

    [Fact]
    public async Task GetDurableStates_Should_Return_Only_Durable_Statuses_And_Honor_MaxPlusOne()
    {
        var source = new DataSource(
            Guid.NewGuid(),
            $"durable-source-{Guid.NewGuid():N}",
            DataSourceType.RelationalDatabase,
            "{}");
        var durablePipelines = Enumerable.Range(0, 4)
            .Select(index => CreatePipeline(source, $"durable-{index}"))
            .ToArray();
        var terminalPipeline = CreatePipeline(source, "terminal");

        var runningOne = CreateExecution(durablePipelines[0], Hash('A'));
        var runningTwo = CreateExecution(durablePipelines[1], Hash('B'));
        var unknownOne = CreateExecution(durablePipelines[2], Hash('C'));
        unknownOne.MarkOutcomeUnknown(DateTime.UtcNow);
        var unknownTwo = CreateExecution(durablePipelines[3], Hash('D'));
        unknownTwo.MarkOutcomeUnknown(DateTime.UtcNow);

        var succeeded = CreateExecution(terminalPipeline);
        succeeded.Complete(DateTime.UtcNow, processedRows: 7);
        var failed = CreateExecution(terminalPipeline);
        failed.Fail(DateTime.UtcNow);
        var cancelled = CreateExecution(terminalPipeline);
        cancelled.Cancel(DateTime.UtcNow);

        await WithUnitOfWorkAsync(async () =>
        {
            await _sourceRepository.InsertAsync(source, autoSave: true);
            await _pipelineRepository.InsertManyAsync(
                durablePipelines.Append(terminalPipeline),
                autoSave: true);
            await _executionRepository.InsertManyAsync(
                new[]
                {
                    runningOne,
                    runningTwo,
                    unknownOne,
                    unknownTwo,
                    succeeded,
                    failed,
                    cancelled,
                },
                autoSave: true);
        });

        const int safeLimit = 2;
        var bounded = await WithUnitOfWorkAsync(() =>
            _executionRepository.GetDurableStatesAsync(safeLimit + 1));

        bounded.Count.ShouldBe(
            safeLimit + 1,
            "仓储必须在 Max+1 截断，让协调器检测到超过安全上限并失败关闭");
        bounded.All(IsDurableState).ShouldBeTrue();

        var allDurable = await WithUnitOfWorkAsync(() =>
            _executionRepository.GetDurableStatesAsync(16));
        var durableIds = new[]
        {
            runningOne.Id,
            runningTwo.Id,
            unknownOne.Id,
            unknownTwo.Id,
        };
        var terminalIds = new[]
        {
            succeeded.Id,
            failed.Id,
            cancelled.Id,
        };

        allDurable.All(IsDurableState).ShouldBeTrue(
            "单次查询只能返回 Running 与 OutcomeUnknown");
        durableIds.All(id => allDurable.Any(item => item.Id == id))
            .ShouldBeTrue("单次查询必须同时覆盖两种耐久状态");
        terminalIds.Any(id => allDurable.Any(item => item.Id == id))
            .ShouldBeFalse("已确认终态不得占用耐久执行槽");
    }

    [Fact]
    public async Task GetDurableStates_Should_Take_StartedAt_Id_Prefix_When_Timestamps_Tie_And_Guids_Were_Inserted_Out_Of_Order()
    {
        var source = new DataSource(
            Guid.NewGuid(),
            $"durable-prefix-source-{Guid.NewGuid():N}",
            DataSourceType.RelationalDatabase,
            "{}");
        var pipelines = Enumerable.Range(0, 5)
            .Select(index => CreatePipeline(source, $"prefix-{index}"))
            .ToArray();
        var tiedStartedAt = new DateTime(
            2000,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        var insertionOrder = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000004"),
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000005"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
        };
        var executions = insertionOrder
            .Select((id, index) => CreateExecution(
                pipelines[index],
                id,
                tiedStartedAt,
                Hash((char)('A' + index))))
            .ToArray();

        await WithUnitOfWorkAsync(async () =>
        {
            await _sourceRepository.InsertAsync(source, autoSave: true);
            await _pipelineRepository.InsertManyAsync(
                pipelines,
                autoSave: true);
            await _executionRepository.InsertManyAsync(
                executions,
                autoSave: true);
        });

        var prefix = await WithUnitOfWorkAsync(() =>
            _executionRepository.GetDurableStatesAsync(3));

        prefix.Select(execution => execution.Id).ShouldBe(
            insertionOrder.OrderBy(id => id).Take(3),
            "Take 必须发生在 StartedAt、Id 全序之后；否则同时间戳行会随插入/物理顺序漂移");
        prefix.ShouldAllBe(execution => execution.StartedAt == tiedStartedAt);
    }

    [Fact]
    public async Task Unresolved_Slots_Should_Remain_Occupied_In_OutcomeUnknown_And_Release_Only_After_Terminal_State()
    {
        var source = new DataSource(
            Guid.NewGuid(),
            $"slot-source-{Guid.NewGuid():N}",
            DataSourceType.RelationalDatabase,
            "{}");
        var firstPipeline = CreatePipeline(source, "slot-first");
        var secondPipeline = CreatePipeline(source, "slot-second");
        var first = CreateExecution(firstPipeline, Hash('A'));

        await WithUnitOfWorkAsync(async () =>
        {
            await _sourceRepository.InsertAsync(source, autoSave: true);
            await _pipelineRepository.InsertManyAsync(
                new[] { firstPipeline, secondPipeline },
                autoSave: true);
            await _executionRepository.InsertAsync(first, autoSave: true);
        });

        await Should.ThrowAsync<DbUpdateException>(() =>
            WithUnitOfWorkAsync(() =>
                _executionRepository.InsertAsync(
                    CreateExecution(firstPipeline, Hash('B')),
                    autoSave: true)));

        await WithUnitOfWorkAsync(async () =>
        {
            var stored = await _executionRepository.GetAsync(first.Id);
            stored.MarkOutcomeUnknown(DateTime.UtcNow);
            await _executionRepository.UpdateAsync(stored, autoSave: true);
        });

        await Should.ThrowAsync<DbUpdateException>(() =>
            WithUnitOfWorkAsync(() =>
                _executionRepository.InsertAsync(
                    CreateExecution(secondPipeline, Hash('A')),
                    autoSave: true)));

        await WithUnitOfWorkAsync(async () =>
        {
            var stored = await _executionRepository.GetAsync(first.Id);
            stored.ResolveAsSucceeded(DateTime.UtcNow);
            await _executionRepository.UpdateAsync(stored, autoSave: true);
        });

        var replacement = CreateExecution(firstPipeline, Hash('A'));
        await WithUnitOfWorkAsync(() =>
            _executionRepository.InsertAsync(replacement, autoSave: true));

        var unresolved = await WithUnitOfWorkAsync(() =>
            _executionRepository.GetDurableStatesAsync(8));
        unresolved.Select(execution => execution.Id)
            .ShouldContain(replacement.Id);
        unresolved.Select(execution => execution.Id)
            .ShouldNotContain(first.Id);
    }

    private static PipelineExecution CreateExecution(DataPipeline pipeline)
        => new(
            Guid.NewGuid(),
            pipeline.Id,
            pipeline.Name,
            TriggerType.Manual);

    private static PipelineExecution CreateExecution(
        DataPipeline pipeline,
        string targetResourceHash)
        => new(
            Guid.NewGuid(),
            pipeline.Id,
            pipeline.Name,
            TriggerType.Manual,
            targetResourceHash);

    private static PipelineExecution CreateExecution(
        DataPipeline pipeline,
        Guid id,
        DateTime startedAt,
        string targetResourceHash)
    {
        var execution = new PipelineExecution(
            id,
            pipeline.Id,
            pipeline.Name,
            TriggerType.Manual,
            targetResourceHash);
        typeof(PipelineExecution)
            .GetProperty(nameof(PipelineExecution.StartedAt))!
            .SetValue(execution, startedAt);
        return execution;
    }

    private static DataPipeline CreatePipeline(
        DataSource source,
        string suffix)
        => new(
            Guid.NewGuid(),
            $"{suffix}-{Guid.NewGuid():N}",
            source.Id);

    private static string Hash(char value) => new(value, 64);

    private static bool IsDurableState(PipelineExecution execution)
        => execution.Status is ExecutionStatus.Running or
            ExecutionStatus.OutcomeUnknown;
}
