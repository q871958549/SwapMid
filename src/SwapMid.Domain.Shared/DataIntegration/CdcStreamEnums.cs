namespace SwapMid.DataIntegration;

/// <summary>
/// PostgreSQL 逻辑复制（pgoutput）CDC 流运行状态。
/// </summary>
public enum CdcStreamStatus
{
    Draft = 0,
    Active = 1,
    Paused = 2,
    Error = 3,
}

/// <summary>
/// CDC 变更事件下游投递目标类型。
/// </summary>
public enum CdcTargetMode
{
    /// <summary>投递到 SwapMid 消息总线主题（CloudEvents 1.0）。</summary>
    MessageBusTopic = 0,

    /// <summary>触发已激活的数据管道同步批/分析型 Sink。</summary>
    DataPipeline = 1,

    /// <summary>直接流式写入外部分析或事件接收端（Kafka / ClickHouse / 对象存储）。</summary>
    ExternalSink = 2,
}

/// <summary>
/// CDC 初始一致全量快照执行状态。
/// </summary>
public enum CdcSnapshotStatus
{
    NotStarted = 0,
    Completed = 1,
    Failed = 2,
}
