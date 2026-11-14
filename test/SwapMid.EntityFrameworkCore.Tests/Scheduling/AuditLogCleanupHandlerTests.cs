using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace SwapMid.Scheduling;

/// <summary>
/// 审计日志清理 handler 冒烟测试：验证 handler 正确接线（<c>[ExposeServices(IScheduledJobHandler)]</c> / DI 可解析）
/// 且演练、正式两路径均返回成功、不抛。删除正确性与 <see cref="ExecutionHistoryCleanupHandler"/> 完全同构
/// （CountAsync + DeleteDirect），由后者的「删旧留新 / 演练不删」测试覆盖；此处不重复造 ABP 内置 AuditLog
/// 数据（构造繁、全仓无先例）。
/// </summary>
[Collection(EntityFrameworkCoreTestCollections.Scheduling)]
public class AuditLogCleanupHandlerTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly AuditLogCleanupHandler _handler;

    public AuditLogCleanupHandlerTests()
    {
        _handler = GetRequiredService<AuditLogCleanupHandler>();
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
