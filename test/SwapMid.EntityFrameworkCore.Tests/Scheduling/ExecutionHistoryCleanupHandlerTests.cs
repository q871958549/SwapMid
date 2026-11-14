using System;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Xunit;

namespace SwapMid.Scheduling;

/// <summary>
/// 执行历史清理 handler 集成测试（SQLite 内存库）：演练只统计不删、正式删旧留新。
/// 保留期取配置缺省 90 天；cutoff 基于 <see cref="IClock"/>.Now（与 recorder 写 StartedAt 同基准，避免时区偏差）。
/// 断言只针对本用例 seed 的特定记录 id，不依赖全表精确条数（集成测试库在用例间共享）。
/// </summary>
[Collection(EntityFrameworkCoreTestCollections.Scheduling)]
public class ExecutionHistoryCleanupHandlerTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly ExecutionHistoryCleanupHandler _handler;
    private readonly IRepository<ScheduledJobExecution, Guid> _repository;
    private readonly IGuidGenerator _guid;
    private readonly IClock _clock;

    public ExecutionHistoryCleanupHandlerTests()
    {
        _handler = GetRequiredService<ExecutionHistoryCleanupHandler>();
        _repository = GetRequiredService<IRepository<ScheduledJobExecution, Guid>>();
        _guid = GetRequiredService<IGuidGenerator>();
        _clock = GetRequiredService<IClock>();
    }

    /// <summary>
    /// 造一条 StartedAt 指定天数前的执行历史。构造签名：(id, jobKey, jobType, jobMethod, hangfireJobId, triggerKind, startedAt)。
    ///
    /// <paramref name="status"/> 默认 <see cref="JobExecutionStatus.Succeeded"/>——**这是有意的**：
    /// 实体构造后天然是 <c>Running</c>，而清理只删终态，用默认构造 seed 会让「该删的没删」这类
    /// 用例全部假通过。要造 Running 就显式传，别让它成为默认。
    /// </summary>
    private async Task<Guid> SeedAsync(int daysAgo, JobExecutionStatus status = JobExecutionStatus.Succeeded)
    {
        var id = _guid.Create();
        var startedAt = _clock.Now.AddDays(-daysAgo);
        var entity = new ScheduledJobExecution(id, "TEST_CLEANUP", "TestJob", "Run", null,
            JobTriggerKind.Scheduled, startedAt);
        switch (status)
        {
            case JobExecutionStatus.Succeeded:
                entity.MarkFinished(startedAt.AddSeconds(1), success: true, "ok", null);
                break;
            case JobExecutionStatus.Failed:
                entity.MarkFinished(startedAt.AddSeconds(1), success: false, null, "boom");
                break;
            case JobExecutionStatus.Interrupted:
                entity.MarkInterrupted(startedAt.AddSeconds(1));
                break;
            case JobExecutionStatus.Skipped:
                entity.MarkSkipped(startedAt.AddSeconds(1));
                break;
            case JobExecutionStatus.Running:
                break; // 构造后即 Running，不收尾
        }

        await _repository.InsertAsync(entity, autoSave: true);
        return id;
    }

    [Fact]
    public async Task 正式_删掉超保留期记录_保留期内保留()
    {
        Guid oldId = default, freshId = default;
        await WithUnitOfWorkAsync(async () =>
        {
            oldId = await SeedAsync(200);  // 200 天前，超 90 天保留期 → 应删
            freshId = await SeedAsync(10); // 10 天前，保留期内 → 应留
        });

        var result = await WithUnitOfWorkAsync(() =>
            _handler.RunAsync(new JobRunContext { TriggerKind = JobTriggerKind.Manual, IsDryRun = false }));

        result.Status.ShouldBe(JobExecutionStatus.Succeeded);

        await WithUnitOfWorkAsync(async () =>
        {
            (await _repository.FindAsync(oldId)).ShouldBeNull();       // 超期的删了
            (await _repository.FindAsync(freshId)).ShouldNotBeNull();  // 保留期内的留着
        });
    }

    /// <summary>
    /// 🔴 **超期的 Running 也不删**（2026-08-04 补，与 MessageRecordCleanupHandler 同一惯例）。
    ///
    /// Running 表示「这条执行还没收尾」——删掉它，那次执行就从台账上凭空消失，而任务本身可能仍在跑。
    /// 正常情况下 Host 启动时的僵尸清理会把超时 Running 标成 Interrupted（届时它自然进入清理范围），
    /// 所以窗口很窄；但删错的代价是审计记录不可恢复，不该赌那个窄窗口。
    ///
    /// 四种终态各造一条一并验证：漏掉任一种就会变成「本该清理的历史永远清不掉」，
    /// 而那个方向的失败同样不报错、只会让表一直涨。
    /// </summary>
    [Fact]
    public async Task 正式_超期的执行中记录不删_四种终态都删()
    {
        Guid runningId = default, succeededId = default, failedId = default,
            interruptedId = default, skippedId = default;
        await WithUnitOfWorkAsync(async () =>
        {
            runningId = await SeedAsync(200, JobExecutionStatus.Running);
            succeededId = await SeedAsync(200, JobExecutionStatus.Succeeded);
            failedId = await SeedAsync(200, JobExecutionStatus.Failed);
            interruptedId = await SeedAsync(200, JobExecutionStatus.Interrupted);
            skippedId = await SeedAsync(200, JobExecutionStatus.Skipped);
        });

        var result = await WithUnitOfWorkAsync(() =>
            _handler.RunAsync(new JobRunContext { TriggerKind = JobTriggerKind.Manual, IsDryRun = false }));

        result.Status.ShouldBe(JobExecutionStatus.Succeeded);

        await WithUnitOfWorkAsync(async () =>
        {
            (await _repository.FindAsync(runningId)).ShouldNotBeNull(
                "执行中的记录即使超期也不能删——它代表一次尚未收尾的执行，删了台账上就凭空少一次");
            (await _repository.FindAsync(succeededId)).ShouldBeNull();
            (await _repository.FindAsync(failedId)).ShouldBeNull();
            (await _repository.FindAsync(interruptedId)).ShouldBeNull();
            (await _repository.FindAsync(skippedId)).ShouldBeNull();
        });
    }

    /// <summary>演练的统计口径必须与正式删除完全一致，否则「演练说删 N 条、正式删了别的量」。</summary>
    [Fact]
    public async Task 演练统计不含执行中记录()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await SeedAsync(200, JobExecutionStatus.Running);
        });

        var result = await WithUnitOfWorkAsync(() =>
            _handler.RunAsync(new JobRunContext { TriggerKind = JobTriggerKind.Manual, IsDryRun = true }));

        result.Summary.ShouldNotBeNull();
        // 摘要须说明「执行中的不删」，否则运维看到「将清理 0 条」会以为清理任务坏了。
        result.Summary!.ShouldContain("执行中的不删");
    }

    [Fact]
    public async Task 演练_只统计不删()
    {
        Guid oldId = default;
        await WithUnitOfWorkAsync(async () =>
        {
            oldId = await SeedAsync(200); // 超期
        });

        var result = await WithUnitOfWorkAsync(() =>
            _handler.RunAsync(new JobRunContext { TriggerKind = JobTriggerKind.Manual, IsDryRun = true }));

        result.Status.ShouldBe(JobExecutionStatus.Succeeded);
        result.Summary.ShouldNotBeNull();
        result.Summary!.ShouldContain("演练");

        await WithUnitOfWorkAsync(async () =>
        {
            (await _repository.FindAsync(oldId)).ShouldNotBeNull(); // 演练没删
        });
    }
}
