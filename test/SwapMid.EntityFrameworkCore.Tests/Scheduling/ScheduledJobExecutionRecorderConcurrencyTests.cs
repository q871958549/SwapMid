using System;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace SwapMid.Scheduling;

/// <summary>
/// 空 JobKey 的手动/系统触发由过滤器记历史。收尾和启动僵尸清理会抢同一条 Running。
/// 先写入的终态留下，后到者不抛 DbUpdateConcurrencyException，也不再插第二条。
/// </summary>
[Collection(EntityFrameworkCoreTestCollections.Scheduling)]
public class ScheduledJobExecutionRecorderConcurrencyTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IScheduledJobExecutionRecorder _recorder;
    private readonly IRepository<ScheduledJobExecution, Guid> _repository;

    public ScheduledJobExecutionRecorderConcurrencyTests()
    {
        _recorder = GetRequiredService<IScheduledJobExecutionRecorder>();
        _repository = GetRequiredService<IRepository<ScheduledJobExecution, Guid>>();
    }

    [Fact]
    public async Task 空JobKey的收尾在已有终态时不覆盖也不再插行()
    {
        var started = await _recorder.StartAsync(
            jobKey: null,
            jobType: "OaManualSyncJob",
            jobMethod: "ExecuteAsync",
            hangfireJobId: "manual-1",
            triggerKind: JobTriggerKind.Manual);
        started.Recorded.ShouldBeTrue();

        (await _recorder.FinishAsync(started.ExecutionId, success: true, summary: "完成", error: null))
            .ShouldBeTrue();
        (await _recorder.InterruptAsync(started.ExecutionId, "进程重启或超时未正常结束，已标记为中断"))
            .ShouldBeTrue();
        (await _recorder.FinishAsync(started.ExecutionId, success: false, summary: null, error: "晚到失败"))
            .ShouldBeTrue();

        await WithUnitOfWorkAsync(async () =>
        {
            (await _repository.CountAsync(x => x.HangfireJobId == "manual-1")).ShouldBe(1);
            var row = await _repository.GetAsync(started.ExecutionId);
            row.JobKey.ShouldBeNull();
            row.Status.ShouldBe(JobExecutionStatus.Succeeded);
            row.Summary.ShouldBe("完成");
            row.Error.ShouldBeNull();
        });
    }

    [Fact]
    public async Task 僵尸清理先写入后收尾保留中断()
    {
        var started = await _recorder.StartAsync(
            jobKey: "",
            jobType: "SystemJob",
            jobMethod: "Execute",
            hangfireJobId: "system-1",
            triggerKind: JobTriggerKind.Manual);
        started.Recorded.ShouldBeTrue();

        (await _recorder.InterruptAsync(started.ExecutionId)).ShouldBeTrue();
        (await _recorder.FinishAsync(started.ExecutionId, success: true, summary: "晚到成功", error: null))
            .ShouldBeTrue();

        await WithUnitOfWorkAsync(async () =>
        {
            (await _repository.CountAsync(x => x.HangfireJobId == "system-1")).ShouldBe(1);
            var row = await _repository.GetAsync(started.ExecutionId);
            row.JobKey.ShouldBe("");
            row.Status.ShouldBe(JobExecutionStatus.Interrupted);
            row.Summary.ShouldNotBe("晚到成功");
        });
    }
}
