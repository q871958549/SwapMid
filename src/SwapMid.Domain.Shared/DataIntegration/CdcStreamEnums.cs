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

    /// <summary>MySQL / MariaDB 二进制日志行级复制（Binlog ROW Image）。</summary>
    MySqlBinlog = 2,

    /// <summary>Push Webhook 认证推送事件流（HMAC-SHA256 签名直推）。</summary>
    PushWebhook = 3,

    /// <summary>Apache Kafka / Redpanda Debezium 信封入站事件流（消费既有 CDC Topic）。</summary>
    KafkaDebezium = 4,
}

/// <summary>
/// 对象存储 Lakehouse 开放表格式提交协议。
/// </summary>
public enum LakehouseTableFormat
{
    /// <summary>仅写入 SwapMid _swapmid/manifests 批次清单与 latest.json 索引。</summary>
    SwapMidManifest = 0,

    /// <summary>同时提交 Apache Delta Lake 事务日志（_delta_log/*.json）及 OPTIMIZE 合并记录。</summary>
    DeltaLake = 1,

    /// <summary>同时提交 Apache Iceberg v2 表元数据（metadata/v*.metadata.json + 二进制 Avro 清单）与版本指针（version-hint.text）。</summary>
    ApacheIceberg = 2,
}

/// <summary>
/// Apache Iceberg v2 表隐藏分区演进策略（Hidden Partitioning）。
/// </summary>
public enum LakehousePartitionStrategy
{
    /// <summary>未分区（partition-spec id=0）。</summary>
    None = 0,

    /// <summary>按提交时间 UTC 自然日隐藏分区（day(commit_timestamp_utc)，spec-id=1，field-id=1000）。</summary>
    DailyCommitUtc = 1,
}

/// <summary>
/// Apache Iceberg v2 表写入模式（追加变更日志 vs Merge-on-Read 键值 Upsert / Equality Deletes）。
/// </summary>
public enum LakehouseWriteMode
{
    /// <summary>追加变更日志（仅写入 DATA 分片，保留全部历史操作事件）。</summary>
    AppendChangelog = 0,

    /// <summary>Merge-on-Read 主键 Upsert（按业务主键写入 Iceberg v2 Equality Delete 文件 content=2，查询引擎自动去重并过滤已删除行）。</summary>
    MergeOnReadUpsert = 1,
}

/// <summary>
/// Lakehouse 外部开放元数据目录（Open Catalog REST）同步协议类型。
/// </summary>
public enum LakehouseCatalogType
{
    /// <summary>不同步到外部元数据目录，仅保留对象存储本地 Manifest / _delta_log。</summary>
    None = 0,

    /// <summary>Databricks / Apache Unity Catalog OSS REST API（/api/2.1/unity-catalog）。</summary>
    UnityCatalogRest = 1,

    /// <summary>Apache Iceberg REST Catalog API（/v1/namespaces/{ns}/tables）。</summary>
    IcebergRest = 2,
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

/// <summary>
/// CDC 数据契约与新鲜度 SLA 评估状态。
/// </summary>
public enum CdcContractStatus
{
    /// <summary>未配置数据契约阈值。</summary>
    Unconfigured = 0,

    /// <summary>所有契约项均达标。</summary>
    Healthy = 1,

    /// <summary>接近新鲜度/滞留阈值警戒线（>=80%）。</summary>
    Warning = 2,

    /// <summary>发生数据契约违约（新鲜度超限、必填列缺失、Schema 漂移或 WAL 滞留超额）。</summary>
    Breached = 3,
}
