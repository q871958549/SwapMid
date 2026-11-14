using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.DataIntegration;

public interface IPipelineExecutionRepository : IRepository<PipelineExecution, Guid>
{
    Task<List<PipelineExecution>> GetListAsync(
        Guid? pipelineId = null,
        ExecutionStatus? status = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.StartedAt,
        string? sorting = null,
        int maxResultCount = 20,
        int skipCount = 0,
        CancellationToken cancellationToken = default);

    Task<int> GetCountAsync(
        Guid? pipelineId = null,
        ExecutionStatus? status = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.StartedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 单一一致性查询读取全部持久执行槽（Running/OutcomeUnknown）。调用方传入安全上限 + 1，
    /// 以便在不做无界物化的前提下失败关闭。
    /// </summary>
    Task<List<PipelineExecution>> GetDurableStatesAsync(
        int maxResultCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在当前事务内只按主键读取并锁定单条执行记录。生产实现必须使用参数化
    /// <c>SELECT ... WHERE Id = @id FOR UPDATE</c>，状态与恢复请求分类只能在锁后进行。
    /// </summary>
    Task<PipelineExecution> GetForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
