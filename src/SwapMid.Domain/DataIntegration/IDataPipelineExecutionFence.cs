using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.DataIntegration;

/// <summary>
/// 数据管道执行在平台 PostgreSQL 上的跨实例最终准入栅栏。实现必须使用独立物理连接持有
/// session advisory lock，并一次性持有 pipeline 与有界目标资源集合；不能借用跨外部 ETL 的长事务。
/// </summary>
public interface IDataPipelineExecutionFence
{
    Task<DataPipelineExecutionFenceAcquireResult> TryAcquireAsync(
        Guid pipelineId,
        IReadOnlyCollection<string> targetResources,
        int capacitySlots,
        CancellationToken cancellationToken = default);
}

/// <summary>执行栅栏一次准入允许携带的目标资源键边界。</summary>
public static class DataPipelineExecutionFenceLimits
{
    /// <summary>
    /// receipt-v1 迁移期同时锁定 legacy 与 verified 两个目标身份；禁止无界扩张锁集合。
    /// </summary>
    public const int MaxTargetResources = 2;
}

public enum DataPipelineExecutionFenceRejection
{
    None = 0,
    ResourceBusy = 1,
    CapacityExhausted = 2,
}

public sealed record DataPipelineExecutionFenceAcquireResult(
    IDataPipelineExecutionFenceHandle? Handle,
    DataPipelineExecutionFenceRejection Rejection)
{
    public static DataPipelineExecutionFenceAcquireResult Acquired(
        IDataPipelineExecutionFenceHandle handle)
        => new(handle, DataPipelineExecutionFenceRejection.None);

    public static DataPipelineExecutionFenceAcquireResult Rejected(
        DataPipelineExecutionFenceRejection rejection)
        => new(null, rejection);
}

/// <summary>持有管道与全部目标资源栅栏，并在物理连接失效时发出取消信号。</summary>
public interface IDataPipelineExecutionFenceHandle : IAsyncDisposable
{
    CancellationToken HandleLostToken { get; }
}
