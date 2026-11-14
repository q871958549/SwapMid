using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.DataIntegration;

/// <summary>管道执行历史记录</summary>
public class PipelineExecutionDto : EntityDto<Guid>
{
    /// <summary>乐观并发戳；执行恢复等状态变更必须回传最新值。</summary>
    public string ConcurrencyStamp { get; set; } = null!;
    /// <summary>所属管道 ID</summary>
    public Guid PipelineId { get; set; }
    /// <summary>所属管道名称</summary>
    public string PipelineName { get; set; } = null!;
    /// <summary>执行状态</summary>
    public ExecutionStatus Status { get; set; }
    /// <summary>触发方式</summary>
    public TriggerType TriggerType { get; set; }
    /// <summary>执行开始时间</summary>
    public DateTime StartedAt { get; set; }
    /// <summary>执行结束时间</summary>
    public DateTime? FinishedAt { get; set; }
    /// <summary>耗时(秒)</summary>
    public double? DurationSeconds { get; set; }
    /// <summary>处理行数</summary>
    public long? ProcessedRows { get; set; }
    /// <summary>稳定错误码（仅失败时返回）</summary>
    public string? ErrorCode { get; set; }
    /// <summary>本地化安全错误信息；不回显底层异常与历史原始错误</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>请求关联号（用于管理员定位日志）</summary>
    public string? CorrelationId { get; set; }
    /// <summary>异常类型全名（不含消息与堆栈）</summary>
    public string? ExceptionType { get; set; }
    /// <summary>执行时目标数据源 ID 快照</summary>
    public Guid? TargetDataSourceId { get; set; }
    /// <summary>执行时目标数据源名称快照</summary>
    public string? TargetDataSourceName { get; set; }
    /// <summary>执行时目标数据库 provider 快照</summary>
    public string? TargetProvider { get; set; }
    /// <summary>执行时目标端点快照（不含账号与凭据）</summary>
    public string? TargetEndpoint { get; set; }
    /// <summary>执行时目标数据库名快照</summary>
    public string? TargetDatabase { get; set; }
    /// <summary>执行时规范目标表快照</summary>
    public string? TargetTable { get; set; }
    /// <summary>执行时目标数据源乐观并发版本</summary>
    public string? TargetConfigVersion { get; set; }
    /// <summary>执行时管理员维护的目标物理资源组</summary>
    public string? TargetResourceGroup { get; set; }
    /// <summary>执行时源数据源 ID 快照</summary>
    public Guid? SourceDataSourceId { get; set; }
    /// <summary>执行时源数据源名称快照</summary>
    public string? SourceDataSourceName { get; set; }
    /// <summary>执行时源数据源乐观并发版本</summary>
    public string? SourceConfigVersion { get; set; }
    /// <summary>执行时写入模式（append / truncate）</summary>
    public string? WriteMode { get; set; }
    /// <summary>执行时规范化源查询的 SHA-256；不回显查询正文</summary>
    public string? SourceQuerySha256 { get; set; }
    /// <summary>执行使用的对账策略（manual-v0 / target-receipt-v1）。</summary>
    public string ReconciliationStrategy { get; set; } = "manual-v0";
    /// <summary>当前执行是否具备机器核验目标回执的完整稳定身份。</summary>
    public bool ReceiptVerificationEligible { get; set; }
    /// <summary>目标回执完成机器核验的时间。</summary>
    public DateTimeOffset? ReceiptVerifiedAt { get; set; }
    /// <summary>机器核验来源；当前仅 target-receipt-v1。</summary>
    public string? ReceiptVerificationSource { get; set; }
    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }
}
