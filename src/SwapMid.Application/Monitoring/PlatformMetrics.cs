using Prometheus;

namespace SwapMid.Monitoring;

/// <summary>
/// 平台业务语义指标（Prometheus，暴露在 /metrics，统一 swapmid_ 前缀）。
/// </summary>
public static partial class PlatformMetrics
{
    public static readonly Counter HangfireJobsFailed = Metrics.CreateCounter(
        "swapmid_hangfire_jobs_failed_total",
        "Hangfire 后台作业最终失败次数（重试耗尽进 FailedState）",
        new CounterConfiguration { LabelNames = ["job_type"] });

    public static readonly Counter OutRateLimitRejections = Metrics.CreateCounter(
        "swapmid_out_rate_limit_rejections_total",
        "对外端点限流拒绝次数（固定低基数标签 policy/kind）",
        new CounterConfiguration { LabelNames = ["policy", "kind"] });

    public static readonly Counter MessageDeliveryTotal = Metrics.CreateCounter(
        "swapmid_message_delivery_total",
        "消息投递结果计数（按订阅逐条投递，标签 result=success/failure）",
        new CounterConfiguration { LabelNames = ["result"] });

    public static readonly Histogram MessageDeliveryDuration = Metrics.CreateHistogram(
        "swapmid_message_delivery_duration_seconds",
        "单次消息投递耗时（秒，含成功与失败）");

    public static readonly Counter MessageDeliveryAbandonedTotal = Metrics.CreateCounter(
        "swapmid_message_delivery_abandoned_total",
        "消息投递因订阅删除、禁用或不再匹配而终止的 attempt 数（固定低基数标签 reason）",
        new CounterConfiguration { LabelNames = ["reason"] });

    public static readonly Histogram EfCommandDuration = Metrics.CreateHistogram(
        "swapmid_ef_command_duration_seconds",
        "EF Core 数据库命令执行耗时（秒，标签 context/operation）",
        new HistogramConfiguration
        {
            LabelNames = ["context", "operation"],
            Buckets = Histogram.ExponentialBuckets(0.005, 2, 12),
        });

    public static readonly Counter EfSlowCommands = Metrics.CreateCounter(
        "swapmid_ef_slow_commands_total",
        "超出慢查询阈值的 EF Core 命令次数（标签 context/operation）",
        new CounterConfiguration { LabelNames = ["context", "operation"] });

    public static readonly Counter EfCommandsFailed = Metrics.CreateCounter(
        "swapmid_ef_commands_failed_total",
        "执行失败或超时的 EF Core 命令次数（标签 context/operation）",
        new CounterConfiguration { LabelNames = ["context", "operation"] });

    public static readonly Gauge ScheduledJobLastStatus = Metrics.CreateGauge(
        "swapmid_scheduled_job_last_status",
        "每个定时任务最近一次非演练业务终态（1=成功，0=失败；不含执行中、跳过、中断、演练）",
        new GaugeConfiguration { LabelNames = ["job_key"] });

    public static readonly Gauge ScheduledJobLastFailureTimestampSeconds = Metrics.CreateGauge(
        "swapmid_scheduled_job_last_failure_timestamp_seconds",
        "每个定时任务最近一次非演练失败的结束时间（Unix 秒）；成功时为 0",
        new GaugeConfiguration { LabelNames = ["job_key"] });

    public static readonly Counter EtlExecutionAdmissionTotal = Metrics.CreateCounter(
        "swapmid_etl_execution_admission_total",
        "ETL 执行准入计数（固定低基数标签 outcome）",
        new CounterConfiguration { LabelNames = ["outcome"] });

    public static readonly Counter EtlExecutionCoordinationEventsTotal = Metrics.CreateCounter(
        "swapmid_etl_execution_coordination_events_total",
        "ETL 执行权、数据库 session fence 与状态投影协调事件（固定低基数标签 outcome）",
        new CounterConfiguration { LabelNames = ["outcome"] });

    public static readonly Counter EtlExecutionResultsTotal = Metrics.CreateCounter(
        "swapmid_etl_execution_results_total",
        "ETL 执行状态结算结果（固定低基数标签 result）",
        new CounterConfiguration { LabelNames = ["result"] });

    public static readonly Counter ApiGatewayRequestsTotal = Metrics.CreateCounter(
        "swapmid_gateway_requests_total",
        "API 网关转发与事件接入计数（固定低基数标签 outcome/status_class）",
        new CounterConfiguration { LabelNames = ["outcome", "status_class"] });

    public static readonly Histogram ApiGatewayUpstreamDuration = Metrics.CreateHistogram(
        "swapmid_gateway_upstream_duration_seconds",
        "API 网关上游转发耗时（秒）",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.005, 2, 12),
        });

    public static readonly Counter CdcEventsCapturedTotal = Metrics.CreateCounter(
        "swapmid_cdc_events_captured_total",
        "PostgreSQL CDC（pgoutput）成功捕获的变更事件数（固定低基数标签 operation=insert/update/delete）",
        new CounterConfiguration { LabelNames = ["operation"] });

    public static readonly Counter CdcCapturesTotal = Metrics.CreateCounter(
        "swapmid_cdc_captures_total",
        "PostgreSQL CDC 单次增量捕获执行计数（固定低基数标签 result=succeeded/failed）",
        new CounterConfiguration { LabelNames = ["result"] });

    public static readonly Histogram CdcCaptureDuration = Metrics.CreateHistogram(
        "swapmid_cdc_capture_duration_seconds",
        "PostgreSQL CDC 单次有界捕获耗时（秒）",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.01, 2, 10),
        });

    public static readonly Gauge CdcWalLagBytes = Metrics.CreateGauge(
        "swapmid_cdc_wal_lag_bytes",
        "PostgreSQL CDC 逻辑复制槽当前保留的 WAL 滞留字节数（标签 stream）",
        new GaugeConfiguration { LabelNames = ["stream"] });

    public static readonly Counter CdcDeliveriesTotal = Metrics.CreateCounter(
        "swapmid_cdc_deliveries_total",
        "PostgreSQL CDC 下游投递批次计数（固定低基数标签 target_mode）",
        new CounterConfiguration { LabelNames = ["target_mode"] });

    public static readonly Counter CdcSchemaDriftEventsTotal = Metrics.CreateCounter(
        "swapmid_cdc_schema_drift_events_total",
        "PostgreSQL CDC 上游表结构漂移护栏触发与确认计数（固定低基数标签 action）",
        new CounterConfiguration { LabelNames = ["action"] });

    public static readonly Counter CdcObjectStorageManifestsTotal = Metrics.CreateCounter(
        "swapmid_cdc_object_storage_manifests_total",
        "PostgreSQL CDC 对象存储 Lakehouse Manifest 写入计数（固定低基数标签 format）",
        new CounterConfiguration { LabelNames = ["format"] });

    public static readonly Counter CdcLakehouseCompactionsTotal = Metrics.CreateCounter(
        "swapmid_cdc_lakehouse_compactions_total",
        "CDC 对象存储 Lakehouse 小文件压缩合并执行次数（固定低基数标签 format/outcome）",
        new CounterConfiguration { LabelNames = ["format", "outcome"] });

    public static readonly Counter CdcLakehouseCompactedFilesTotal = Metrics.CreateCounter(
        "swapmid_cdc_lakehouse_compacted_files_total",
        "CDC 对象存储 Lakehouse 累计被合并的小文件分片数（固定低基数标签 format）",
        new CounterConfiguration { LabelNames = ["format"] });

    public static readonly Counter CdcCatalogSyncsTotal = Metrics.CreateCounter(
        "swapmid_cdc_catalog_syncs_total",
        "CDC Lakehouse 外部开放目录（Unity Catalog / Iceberg REST）元数据同步计数（固定低基数标签 catalog_type/outcome）",
        new CounterConfiguration { LabelNames = ["catalog_type", "outcome"] });

    public static readonly Counter CdcContractEvaluationsTotal = Metrics.CreateCounter(
        "swapmid_cdc_contract_evaluations_total",
        "CDC 数据契约与新鲜度 SLA 评估结果计数（固定低基数标签 outcome）",
        new CounterConfiguration { LabelNames = ["outcome"] });

    public static readonly Counter CdcContractViolationsTotal = Metrics.CreateCounter(
        "swapmid_cdc_contract_violations_total",
        "CDC 数据契约违约项计数（固定低基数标签 violation_type）",
        new CounterConfiguration { LabelNames = ["violation_type"] });

    public static readonly Counter CdcLakehousePreviewsTotal = Metrics.CreateCounter(
        "swapmid_cdc_lakehouse_previews_total",
        "Lakehouse 零外部计算集群实时数据预览执行计数（固定低基数标签 format）",
        new CounterConfiguration { LabelNames = ["format"] });

    public static readonly Counter CdcIcebergCommitsTotal = Metrics.CreateCounter(
        "swapmid_cdc_iceberg_commits_total",
        "Apache Iceberg v2 表元数据快照原子提交计数（固定低基数标签 operation/outcome）",
        new CounterConfiguration { LabelNames = ["operation", "outcome"] });

    public static readonly Counter CdcWebhookIngestsTotal = Metrics.CreateCounter(
        "swapmid_cdc_webhook_ingests_total",
        "Push Webhook CDC 认证推送接入结果计数（固定低基数标签 outcome）",
        new CounterConfiguration { LabelNames = ["outcome"] });

    public static readonly Gauge CdcKafkaLagMessages = Metrics.CreateGauge(
        "swapmid_cdc_kafka_lag_messages",
        "Kafka Debezium CDC 源当前各分区累计未消费消息滞留条数（标签 stream）",
        new GaugeConfiguration { LabelNames = ["stream"] });

    public static readonly Counter CdcIcebergEqualityDeletesTotal = Metrics.CreateCounter(
        "swapmid_cdc_iceberg_equality_deletes_total",
        "Apache Iceberg v2 Merge-on-Read 写入的 Equality Delete 键值条目总数");

    public static readonly Counter CdcSchemaRegistryFetchesTotal = Metrics.CreateCounter(
        "swapmid_cdc_schema_registry_fetches_total",
        "Confluent / Redpanda Schema Registry 拉取次数（标签 outcome: cache_hit|fetched|failed）",
        new CounterConfiguration { LabelNames = ["outcome"] });

    public static readonly Counter CdcIcebergMaintenanceTotal = Metrics.CreateCounter(
        "swapmid_cdc_iceberg_maintenance_total",
        "Apache Iceberg v2 表维护操作计数（标签 operation=tag|expire|promote|rollback|schema_evolve, outcome=succeeded|failed）",
        new CounterConfiguration { LabelNames = ["operation", "outcome"] });

    public static readonly Gauge CdcLakehouseActiveBytes = Metrics.CreateGauge(
        "swapmid_cdc_lakehouse_active_bytes",
        "Lakehouse 当前活跃数据分片总字节数（FinOps 存储计量，标签 stream_name/table_format）",
        new GaugeConfiguration { LabelNames = ["stream_name", "table_format"] });

    public static readonly Gauge CdcLakehouseActiveFiles = Metrics.CreateGauge(
        "swapmid_cdc_lakehouse_active_files",
        "Lakehouse 当前活跃数据分片文件数（FinOps 存储计量，标签 stream_name/table_format）",
        new GaugeConfiguration { LabelNames = ["stream_name", "table_format"] });

    public static readonly Gauge CdcLakehouseSnapshotsCount = Metrics.CreateGauge(
        "swapmid_cdc_lakehouse_snapshots_count",
        "Lakehouse 当前元数据保留快照总数（标签 stream_name/table_format）",
        new GaugeConfiguration { LabelNames = ["stream_name", "table_format"] });

    public static readonly Counter OpenLineageEventsTotal = Metrics.CreateCounter(
        "swapmid_openlineage_events_total",
        "CNCF OpenLineage 2.0 外部目录事件推送计数（标签 event_type/outcome）",
        new CounterConfiguration { LabelNames = ["event_type", "outcome"] });

    private static readonly string[] EtlExecutionAdmissionOutcomes =
    [
        "acquired", "busy", "idempotent_replay", "coordination_failure",
        "capacity_exhausted",
    ];

    private static readonly string[] EtlExecutionCoordinationOutcomes =
    [
        "database_fence_failure", "database_fence_conflict",
        "target_commit_outcome_unknown", "ownership_lost",
        "database_fence_release_failure", "lock_release_failure",
        "running_durable_state_blocked", "state_commit_outcome_unknown",
        "projection_write_failure", "target_quarantine_blocked",
        "legacy_durable_state_blocked", "durable_state_scan_limit",
        "terminal_store_topology_mismatch",
        "terminal_store_topology_probe_failure",
        "running_recovery_succeeded",
        "running_recovery_late_finalize_blocked",
        "target_receipt_absent",
        "target_receipt_mismatch",
        "target_receipt_unavailable",
        "target_receipt_verified",
        "target_receipt_manual_override_blocked",
    ];

    private static readonly string[] EtlExecutionResults =
    [
        "succeeded", "failed", "cancelled", "outcome_unknown", "unexpected",
    ];

    static PlatformMetrics()
    {
        PublishEtlExecutionSeries(
            EtlExecutionAdmissionTotal,
            EtlExecutionCoordinationEventsTotal,
            EtlExecutionResultsTotal);
        PublishSingleLabelSeries(CdcEventsCapturedTotal, ["insert", "update", "delete"]);
        PublishSingleLabelSeries(CdcCapturesTotal, ["succeeded", "failed"]);
        PublishSingleLabelSeries(CdcDeliveriesTotal, ["message_bus_topic", "data_pipeline", "kafka", "clickhouse", "object_storage", "relational_table"]);
        PublishSingleLabelSeries(CdcSchemaDriftEventsTotal, ["warn", "paused", "accepted"]);
        PublishSingleLabelSeries(CdcObjectStorageManifestsTotal, ["parquet", "jsonl"]);
        PublishSingleLabelSeries(CdcLakehouseCompactedFilesTotal, ["parquet", "jsonl"]);
        foreach (var format in new[] { "parquet", "jsonl" })
        {
            foreach (var outcome in new[] { "succeeded", "skipped", "failed" })
            {
                CdcLakehouseCompactionsTotal.WithLabels(format, outcome).Publish();
            }
        }

        foreach (var catalogType in new[] { "unity_catalog", "iceberg_rest" })
        {
            foreach (var outcome in new[] { "succeeded", "failed", "skipped" })
            {
                CdcCatalogSyncsTotal.WithLabels(catalogType, outcome).Publish();
            }
        }

        PublishSingleLabelSeries(CdcContractEvaluationsTotal, ["unconfigured", "healthy", "warning", "breached"]);
        PublishSingleLabelSeries(CdcContractViolationsTotal, ["freshness", "required_columns", "wal_lag", "schema_drift"]);
        PublishSingleLabelSeries(CdcLakehousePreviewsTotal, ["parquet", "jsonl"]);
        foreach (var op in new[] { "append", "overwrite", "replace" })
        {
            foreach (var outcome in new[] { "succeeded", "retried", "failed" })
            {
                CdcIcebergCommitsTotal.WithLabels(op, outcome).Publish();
            }
        }

        CdcIcebergEqualityDeletesTotal.Publish();
        PublishSingleLabelSeries(CdcSchemaRegistryFetchesTotal, ["cache_hit", "fetched", "failed"]);
        foreach (var op in new[] { "tag", "expire", "promote", "rollback", "schema_evolve" })
        {
            foreach (var outcome in new[] { "succeeded", "failed" })
            {
                CdcIcebergMaintenanceTotal.WithLabels(op, outcome).Publish();
            }
        }

        foreach (var ev in new[] { "promote", "rollback", "schema_evolve" })
        {
            foreach (var outcome in new[] { "succeeded", "failed", "skipped" })
            {
                OpenLineageEventsTotal.WithLabels(ev, outcome).Publish();
            }
        }

        PublishSingleLabelSeries(CdcWebhookIngestsTotal, ["accepted", "replayed", "rejected_auth", "rejected_inactive", "rejected_payload", "failed"]);
    }

    internal static void PublishEtlExecutionSeries(
        Counter admissionCounter,
        Counter coordinationCounter,
        Counter resultCounter)
    {
        PublishSingleLabelSeries(admissionCounter, EtlExecutionAdmissionOutcomes);
        PublishSingleLabelSeries(coordinationCounter, EtlExecutionCoordinationOutcomes);
        PublishSingleLabelSeries(resultCounter, EtlExecutionResults);
    }

    private static void PublishSingleLabelSeries(Counter counter, string[] values)
    {
        foreach (var value in values)
        {
            counter.WithLabels(value).Publish();
        }
    }
}
