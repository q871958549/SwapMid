using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.DataIntegration;

/// <summary>流式 ETL 抽取向非关系型外部接收端传递的有界批次。</summary>
public sealed class EtlSourceBatch
{
    internal EtlSourceBatch(
        string[] targetColumns,
        Type[] targetClrTypes,
        IReadOnlyList<object?[]> rows,
        long batchIndex)
    {
        TargetColumns = targetColumns;
        TargetClrTypes = targetClrTypes;
        Rows = rows;
        BatchIndex = batchIndex;
    }

    internal string[] TargetColumns { get; }

    internal Type[] TargetClrTypes { get; }

    internal IReadOnlyList<object?[]> Rows { get; }

    internal long BatchIndex { get; }
}

/// <summary>可插拔外部数据分析/事件流接收端写入契约（ClickHouse / ObjectStorage / Kafka）。</summary>
public interface IEtlSinkWriter
{
    DataSourceType TargetType { get; }

    Task<long> WriteAsync(
        Guid pipelineId,
        Guid runId,
        DataSource target,
        EtlTransformConfig transform,
        Func<Func<EtlSourceBatch, CancellationToken, Task>, CancellationToken, Task> streamBatchesAsync,
        CancellationToken cancellationToken);
}
