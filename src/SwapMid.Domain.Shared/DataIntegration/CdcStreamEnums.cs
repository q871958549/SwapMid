namespace SwapMid.DataIntegration;

/// <summary>
/// CDC 上游数据源捕获协议类型。
/// </summary>
public enum CdcSourceType
{
    /// <summary>PostgreSQL 逻辑复制（pgoutput WAL）。</summary>
    PostgreSqlLogicalReplication = 0,

    /// <summary>SQL Server 原生变更跟踪（CHANGETABLE(CHANGES ...)）。</summary>
    SqlServerChangeTracking = 1,
}

/// <summary>
/// PostgreSQL 逻辑复制（pgoutput）与多源 CDC 流运行状态。
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

    /// <summary>按业务主键直接行级物化（Upsert / Delete）至下游关系型数据库表。</summary>
    RelationalTable = 3,
}

/// <summary>
/// 关系型目标表镜像（RelationalTable）遇到上游 DELETE 事件时的处理策略。
/// </summary>
public enum CdcDeletePolicy
{
    DeleteRow = 0,
    Ignore = 1,
    SoftDelete = 2,
}

/// <summary>
/// 上游监控表发生 DDL / Schema 漂移时的护栏处理策略。
/// </summary>
public enum CdcSchemaDriftPolicy
{
    Ignore = 0,
    Warn = 1,
    PauseAndAlert = 2,
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
