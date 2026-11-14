using System;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace SwapMid.Scheduling;

/// <summary>
/// <see cref="RunScheduledJobBackgroundJob"/> 兼容测试：验证后台 Job → Runner → Handler 链路
/// 以及存量超长空白参数的边界校验。
/// </summary>
[Collection(EntityFrameworkCoreTestCollections.Scheduling)]
public class RunScheduledJobBackgroundJobTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly RunScheduledJobBackgroundJob _job;
    private readonly IRepository<ScheduledJobExecution, Guid> _executions;

    public RunScheduledJobBackgroundJobTests()
    {
        _job = GetRequiredService<RunScheduledJobBackgroundJob>();
        _executions = GetRequiredService<IRepository<ScheduledJobExecution, Guid>>();
    }

    [Fact]
    public async Task ExecuteAsync_经Runner驱动handler_正式执行成功并记手动历史()
    {
        var argsJson = "{\"note\":\"probe\"}";
        await _job.ExecuteAsync(new RunScheduledJobArgs
        {
            JobKey = ScheduledJobKeys.AuditLogCleanup,
            ArgsJson = argsJson,
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var execs = await _executions.GetListAsync(x => x.JobKey == ScheduledJobKeys.AuditLogCleanup);
            execs.ShouldContain(e => e.TriggerKind == JobTriggerKind.Manual
                && !e.IsDryRun
                && e.Status == JobExecutionStatus.Succeeded
                && e.ArgsJson == argsJson);
        });
    }

    [Fact]
    public async Task ExecuteAsync_存量超长空白参数不得归一为空后绕过边界()
    {
        var argsJson = new string(
            ' ',
            JobRunContext.MaxArgsJsonCharacters + 1);

        var exception = await Should.ThrowAsync<ScheduledJobRecordedFailureException>(
            () => _job.ExecuteAsync(new RunScheduledJobArgs
            {
                JobKey = ScheduledJobKeys.AuditLogCleanup,
                ArgsJson = argsJson,
            }));
        exception.TerminalStateRecorded.ShouldBeTrue();

        await WithUnitOfWorkAsync(async () =>
        {
            var execs = await _executions.GetListAsync(
                x => x.JobKey == ScheduledJobKeys.AuditLogCleanup);
            execs.ShouldContain(e =>
                e.Status == JobExecutionStatus.Failed
                && e.ArgsJson != null
                && e.ArgsJson.Length == JobRunContext.MaxArgsJsonCharacters
                && (e.Summary ?? string.Empty).Contains("参数过长"));
        });
    }
}
