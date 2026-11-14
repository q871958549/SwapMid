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
