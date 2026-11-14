using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.DataIntegration;

public interface IDataPipelineRepository : IRepository<DataPipeline, Guid>
{
    /// <summary>
    /// 在当前事务内先锁定管道行，再按稳定 Guid 顺序锁定源/目标数据源行，
    /// 并返回同一 DbContext 跟踪的激活快照。生产实现只允许 PostgreSQL；
    /// SQLite 分支仅供协议编排测试。
    /// </summary>
    Task<DataPipelineActivationLock> GetActivationForUpdateAsync(
        Guid pipelineId,
        CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task<List<DataPipeline>> GetListAsync(
        string? filter = null,
        Guid? sourceId = null,
        PipelineStatus? status = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        string? sorting = null,
        int maxResultCount = 10,
        int skipCount = 0,
        CancellationToken cancellationToken = default);

    Task<int> GetCountAsync(
        string? filter = null,
        Guid? sourceId = null,
        PipelineStatus? status = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 数据管道激活提交阶段已锁定、由同一 DbContext 跟踪的三行。
/// </summary>
public sealed record DataPipelineActivationLock(
    DataPipeline Pipeline,
    DataSource Source,
    DataSource? Target);
