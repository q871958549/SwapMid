using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace SwapMid.Scheduling;

/// <summary>
/// 通知发送日志清理 handler 冒烟测试：验证 handler 正确接线（<c>[ExposeServices(IScheduledJobHandler)]</c> /
/// DI 可解析）且演练、正式两路径均返回成功、不抛。与 <see cref="AuditLogCleanupHandlerTests"/>/
/// <see cref="MessageRecordCleanupHandlerTests"/> 同理不重造带精确 CreationTime 的测试数据——CreationTime
/// 由 ABP 审计拦截器写入、无构造函数注入点；删除正确性的「删旧留新 / 演练不删」形态已由
/// <see cref="ExecutionHistoryCleanupHandlerTests"/>（StartedAt 走构造函数可控）覆盖，此处不重复。
/// </summary>
[Collection(EntityFrameworkCoreTestCollections.Scheduling)]
public class NotificationLogCleanupHandlerTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly NotificationLogCleanupHandler _handler;

    public NotificationLogCleanupHandlerTests()
    {
        _handler = GetRequiredService<NotificationLogCleanupHandler>();
    }

    [Fact]
    public async Task 演练_返回成功且标演练_不抛()
    {
        var result = await WithUnitOfWorkAsync(() =>
            _handler.RunAsync(new JobRunContext { TriggerKind = JobTriggerKind.Manual, IsDryRun = true }));

        result.Status.ShouldBe(JobExecutionStatus.Succeeded);
        result.Summary.ShouldNotBeNull();
        result.Summary!.ShouldContain("演练");
    }

    [Fact]
    public async Task 正式_返回成功_不抛()
    {
        var result = await WithUnitOfWorkAsync(() =>
            _handler.RunAsync(new JobRunContext { TriggerKind = JobTriggerKind.Manual, IsDryRun = false }));

        result.Status.ShouldBe(JobExecutionStatus.Succeeded);
    }
}
