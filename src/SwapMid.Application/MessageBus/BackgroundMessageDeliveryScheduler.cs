using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace SwapMid.MessageBus;

/// <summary>
/// 投递入队器。<b>UoW 感知</b>:存在环境事务时,把真正的入队挂到 <c>OnCompleted</c>——事务提交后才入队,
/// 保证记录 / 状态先落库,否则 Hangfire 作业可能先于 commit 执行 → 读不到记录(或读到旧状态)。
/// 无环境事务则立即入队。统一修「事务内入队」竞态,惠及 UI 发布 / 重推、内部 publisher、作业重试全部调用方。
/// </summary>
public class BackgroundMessageDeliveryScheduler : IMessageDeliveryScheduler, ITransientDependency
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public BackgroundMessageDeliveryScheduler(
        IServiceScopeFactory serviceScopeFactory,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public Task EnqueueAsync(Guid messageRecordId, TimeSpan? delay = null)
    {
        var uow = _unitOfWorkManager.Current;
        if (uow is not null)
        {
            uow.OnCompleted(() => EnqueueCoreAsync(messageRecordId, delay));
            return Task.CompletedTask;
        }

        return EnqueueCoreAsync(messageRecordId, delay);
    }

    // ⚠️ 从新建 scope 解析 job manager:OnCompleted 触发时原请求作用域可能已释放,不得闭包捕获作用域实例。
    private async Task EnqueueCoreAsync(Guid messageRecordId, TimeSpan? delay)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var jobManager = scope.ServiceProvider.GetRequiredService<IBackgroundJobManager>();
        await jobManager.EnqueueAsync(
            new MessageDeliveryJobArgs { MessageRecordId = messageRecordId },
            delay: delay);
    }
}
