using System;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

/// <summary>
/// 失败终态与通知任务必须在同一事务型 UoW 内写入；仅有 ambient UoW
/// 不足以证明两个 DbContext 会共同提交或回滚。
/// </summary>
public interface IPipelineTerminalUnitOfWorkGuard
{
    void EnsureTransactional();
}

public sealed class PipelineTerminalUnitOfWorkGuard(
    IUnitOfWorkManager unitOfWorkManager) :
    IPipelineTerminalUnitOfWorkGuard,
    ITransientDependency
{
    public void EnsureTransactional()
    {
        if (unitOfWorkManager.Current is
            { Options.IsTransactional: true })
        {
            return;
        }

        throw new InvalidOperationException(
            "ETL failure notification must be enqueued inside the transactional terminal unit of work.");
    }
}
