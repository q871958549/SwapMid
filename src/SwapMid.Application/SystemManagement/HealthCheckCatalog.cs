using System.Collections.Generic;

namespace SwapMid.SystemManagement;

public static class HealthCheckCatalog
{
    public enum HealthCheckKind
    {
        Availability,
        AvailabilityMulti,
        UsagePercent,
        FailureRatio
    }

    public sealed class HealthCheckDefinition
    {
        public required string Name { get; init; }
        public required HealthCheckKind Kind { get; init; }
        public required string PromQl { get; init; }
        public double WarnThreshold { get; init; }
        public double CriticalThreshold { get; init; }
        public required string HealthyConclusion { get; init; }
        public string? WarningConclusion { get; init; }
        public required string CriticalConclusion { get; init; }
        public required string Impact { get; init; }
        public string? WarningSuggestion { get; init; }
        public required string CriticalSuggestion { get; init; }
    }

    private const string SuggestSelfHeal = "通常几分钟内自动恢复；若超过 10 分钟仍未恢复，请检查容器与网络状态。";
    private const string SuggestWatch = "暂不影响正常交换，请持续关注趋势。";
    private const string SuggestCapacity = "请尽快清理历史归档或扩容主机资源。";

    public static readonly IReadOnlyList<HealthCheckDefinition> Items = new List<HealthCheckDefinition>
    {
        new()
        {
            Name = "后端集成服务",
            Kind = HealthCheckKind.Availability,
            PromQl = "max(up{job=\"swapmid-api\"})",
            HealthyConclusion = "后端网关与集成引擎运行正常。",
            CriticalConclusion = "后端服务当前不可达或停止响应。",
            Impact = "管理控制台、API 网关转发、消息投递与管道触发均受影响。",
            CriticalSuggestion = SuggestSelfHeal
        },
        new()
        {
            Name = "HTTP 接口 5xx 错误率",
            Kind = HealthCheckKind.FailureRatio,
            PromQl = "sum(rate(http_request_duration_seconds_count{code=~\"5..\"}[5m])) / sum(rate(http_request_duration_seconds_count[5m]))",
            WarnThreshold = 0.02,
            CriticalThreshold = 0.05,
            HealthyConclusion = "接口响应稳定，无明显服务端异常。",
            WarningConclusion = "少量请求出现 5xx 错误（错误率 {value}）。",
            CriticalConclusion = "服务端 5xx 错误率偏高（错误率 {value}）。",
            Impact = "部分网关调用或管理操作可能失败。",
            WarningSuggestion = SuggestWatch,
            CriticalSuggestion = SuggestSelfHeal
        },
        new()
        {
            Name = "主机内存使用率",
            Kind = HealthCheckKind.UsagePercent,
            PromQl = "(1 - node_memory_MemAvailable_bytes / node_memory_MemTotal_bytes) * 100",
            WarnThreshold = 85,
            CriticalThreshold = 93,
            HealthyConclusion = "主机内存充裕。",
            WarningConclusion = "主机内存使用率偏高（已用 {value}）。",
            CriticalConclusion = "主机内存接近耗尽（已用 {value}）。",
            Impact = "流式 ETL 批次与高并发消息投递可能出现延迟或触发 OOM。",
            WarningSuggestion = SuggestWatch,
            CriticalSuggestion = SuggestCapacity
        },
        new()
        {
            Name = "主机磁盘使用率",
            Kind = HealthCheckKind.UsagePercent,
            PromQl = "max((1 - node_filesystem_avail_bytes{fstype!~\"tmpfs|overlay|squashfs|cifs|nfs|nfs4|fuse.*\"} / node_filesystem_size_bytes{fstype!~\"tmpfs|overlay|squashfs|cifs|nfs|nfs4|fuse.*\"}) * 100)",
            WarnThreshold = 85,
            CriticalThreshold = 92,
            HealthyConclusion = "本地磁盘空间充足。",
            WarningConclusion = "本地磁盘空间偏紧（已用 {value}）。",
            CriticalConclusion = "本地磁盘即将写满（已用 {value}）。",
            Impact = "写满后数据库写入、审计日志与任务调度将失败。",
            WarningSuggestion = SuggestWatch,
            CriticalSuggestion = SuggestCapacity
        },
        new()
        {
            Name = "PostgreSQL 主库",
            Kind = HealthCheckKind.Availability,
            PromQl = "max(pg_up)",
            HealthyConclusion = "PostgreSQL 数据库运行正常。",
            CriticalConclusion = "PostgreSQL 数据库连接异常。",
            Impact = "平台配置、执行协调与消息持久化不可用。",
            CriticalSuggestion = "请立即检查 PostgreSQL 实例与连接池状态。"
        },
        new()
        {
            Name = "Redis 缓存与分布式锁",
            Kind = HealthCheckKind.Availability,
            PromQl = "max(redis_up)",
            HealthyConclusion = "Redis 服务运行正常。",
            CriticalConclusion = "Redis 服务不可用。",
            Impact = "分布式锁、权限缓存与会话状态受影响。",
            CriticalSuggestion = SuggestSelfHeal
        },
        new()
        {
            Name = "RabbitMQ 消息代理",
            Kind = HealthCheckKind.AvailabilityMulti,
            PromQl = "up{job=~\".*rabbitmq.*\"}",
            HealthyConclusion = "RabbitMQ 节点运行正常。",
            CriticalConclusion = "RabbitMQ 节点不可用：{value}。",
            Impact = "RabbitMQ 类型订阅投递将排队等待重连。",
            CriticalSuggestion = SuggestSelfHeal
        },
        new()
        {
            Name = "消息总线投递失败率",
            Kind = HealthCheckKind.FailureRatio,
            PromQl = "sum(rate(swapmid_message_delivery_total{result=\"failure\"}[10m])) / sum(rate(swapmid_message_delivery_total[10m]))",
            WarnThreshold = 0.1,
            CriticalThreshold = 0.3,
            HealthyConclusion = "消息总线各订阅投递顺畅。",
            WarningConclusion = "部分消息投递失败（失败率 {value}）。",
            CriticalConclusion = "大量消息投递失败（失败率 {value}）。",
            Impact = "下游订阅方可能出现通知或事件延迟，请查看消息投递记录重试。",
            WarningSuggestion = SuggestWatch,
            CriticalSuggestion = SuggestSelfHeal
        },
        new()
        {
            Name = "数据管道执行失败率",
            Kind = HealthCheckKind.FailureRatio,
            PromQl = "sum(increase(swapmid_etl_execution_results_total{outcome!=\"succeeded\"}[1h])) / sum(increase(swapmid_etl_execution_results_total[1h]))",
            WarnThreshold = 0.1,
            CriticalThreshold = 0.3,
            HealthyConclusion = "近 1 小时数据管道执行全部成功。",
            WarningConclusion = "部分数据管道执行未成功（异常占比 {value}）。",
            CriticalConclusion = "数据管道大面积执行异常（异常占比 {value}）。",
            Impact = "源端与目标库之间的数据同步可能滞后或需要人工回执核验。",
            WarningSuggestion = SuggestWatch,
            CriticalSuggestion = "请前往「数据集成 → 执行记录」查看失败原因或执行目标回执核验。"
        }
    };
}
