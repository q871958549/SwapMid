using System;
using System.Linq;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.DataIntegration;

/// <summary>管道执行记录（数据管道每次运行的执行历史与结果快照）。</summary>
public class PipelineExecution : CreationAuditedAggregateRoot<Guid>
{
    /// <summary>所属管道 Id。</summary>
    public Guid PipelineId { get; private set; }

    /// <summary>
    /// 目标物理资源摘要。未决状态以此建立数据库级唯一隔离槽；旧终态历史允许为空。
    /// </summary>
    public string? TargetResourceHash { get; private set; }

    /// <summary>
    /// receipt-v1 由目标控制表实例 ID + 数据库规范表名证明的目标摘要。迁移期与旧资源组
    /// 摘要并存，避免 receipt/manual 两种协议失去共同隔离键。
    /// </summary>
    public string? VerifiedTargetResourceHash { get; private set; }

    /// <summary>证据协议：0=manual-v0，1=target-receipt-v1。</summary>
    public short EvidenceProtocol { get; private set; }

    /// <summary>receipt-v1 目标数据库控制表声明的资源实例 ID。</summary>
    public Guid? TargetResourceInstanceId { get; private set; }

    /// <summary>receipt-v1 不可变执行契约 SHA-256。</summary>
    public string? ExecutionContractHash { get; private set; }

    /// <summary>
    /// receipt-v1 激活时证明的可写主节点协调身份 SHA-256。目标写入与核验必须
    /// 在同一身份上复验，拒绝读写分离代理、多主或 DNS 漂移造成的跨节点误判。
    /// </summary>
    public string? TargetCoordinationIdentityHash { get; private set; }

    /// <summary>管道名称文本快照（执行时冗余留存，便于查询展示）。</summary>
    public string PipelineName { get; private set; } = null!;

    /// <summary>执行状态（待执行 / 执行中 / 成功 / 失败 / 已取消 / 结果待核对）。</summary>
    public ExecutionStatus Status { get; private set; }

    /// <summary>触发类型（手动 / 定时）。</summary>
    public TriggerType TriggerType { get; private set; }

    /// <summary>开始时间。</summary>
    public DateTime StartedAt { get; private set; }

    /// <summary>结束时间（可空，运行中为空）。</summary>
    public DateTime? FinishedAt { get; private set; }

    /// <summary>处理行数（可空，成功完成时回填）。</summary>
    public long? ProcessedRows { get; private set; }

    /// <summary>
    /// 历史错误原文（仅为兼容既有数据库列保留）。
    /// 新执行不得写入；对外失败信息由应用层安全失败策略生成。
    /// </summary>
    public string? ErrorMessage { get; private set; }

    private PipelineExecution() { }

    public PipelineExecution(
        Guid id,
        Guid pipelineId,
        string pipelineName,
        TriggerType triggerType,
        string? targetResourceHash = null,
        string? verifiedTargetResourceHash = null,
        short evidenceProtocol = 0,
        Guid? targetResourceInstanceId = null,
        string? executionContractHash = null,
        string? targetCoordinationIdentityHash = null)
        : base(id)
    {
        PipelineId = pipelineId;
        PipelineName = pipelineName;
        TriggerType = triggerType;
        Status = ExecutionStatus.Running;
        StartedAt = DateTime.UtcNow;
        SetEvidenceIdentity(
            targetResourceHash,
            verifiedTargetResourceHash,
            evidenceProtocol,
            targetResourceInstanceId,
            executionContractHash,
            targetCoordinationIdentityHash);
    }

    private void SetEvidenceIdentity(
        string? targetResourceHash,
        string? verifiedTargetResourceHash,
        short evidenceProtocol,
        Guid? targetResourceInstanceId,
        string? executionContractHash,
        string? targetCoordinationIdentityHash)
    {
        if (targetResourceHash != null && !IsSha256Hex(targetResourceHash))
        {
            throw new ArgumentException(
                "Target resource hash must be an uppercase SHA-256 hex value.",
                nameof(targetResourceHash));
        }

        if (verifiedTargetResourceHash != null &&
            !IsSha256Hex(verifiedTargetResourceHash))
        {
            throw new ArgumentException(
                "Verified target resource hash must be an uppercase SHA-256 hex value.",
                nameof(verifiedTargetResourceHash));
        }

        if (evidenceProtocol is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(evidenceProtocol));
        }

        if (evidenceProtocol == 1 &&
            (!IsSha256Hex(targetResourceHash) ||
             verifiedTargetResourceHash is null ||
             targetResourceInstanceId is null ||
             targetResourceInstanceId == Guid.Empty ||
             !IsSha256Hex(executionContractHash) ||
             !IsSha256Hex(targetCoordinationIdentityHash)))
        {
            throw new ArgumentException(
                "Receipt-v1 executions require a resource instance and execution contract hash.");
        }

        if (evidenceProtocol == 0 &&
            (verifiedTargetResourceHash != null ||
             targetResourceInstanceId != null ||
             executionContractHash != null ||
             targetCoordinationIdentityHash != null))
        {
            throw new ArgumentException(
                "Manual-v0 executions cannot carry receipt-v1 identity fields.");
        }

        TargetResourceHash = targetResourceHash;
        VerifiedTargetResourceHash = verifiedTargetResourceHash;
        EvidenceProtocol = evidenceProtocol;
        TargetResourceInstanceId = targetResourceInstanceId;
        ExecutionContractHash = executionContractHash;
        TargetCoordinationIdentityHash = targetCoordinationIdentityHash;
    }

    private static bool IsSha256Hex(string? value) =>
        value is { Length: PipelineExecutionConsts.Sha256HexLength } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');

    public void Complete(DateTime finishedAt, long? processedRows = null)
    {
        EnsureStatus(ExecutionStatus.Running);
        EnsureProcessedRows(processedRows);
        Status = ExecutionStatus.Succeeded;
        FinishedAt = NormalizeFinishedAt(finishedAt);
        ProcessedRows = processedRows;
        ErrorMessage = null;
    }

    public void Fail(DateTime finishedAt)
    {
        EnsureStatus(ExecutionStatus.Running);
        Status = ExecutionStatus.Failed;
        FinishedAt = NormalizeFinishedAt(finishedAt);
        ProcessedRows = null;
        ErrorMessage = null;
    }

    public void Cancel(DateTime finishedAt)
    {
        EnsureStatus(ExecutionStatus.Running);
        Status = ExecutionStatus.Cancelled;
        FinishedAt = NormalizeFinishedAt(finishedAt);
        ProcessedRows = null;
        ErrorMessage = null;
    }

    public void MarkOutcomeUnknown(DateTime finishedAt)
    {
        EnsureStatus(ExecutionStatus.Running);
        Status = ExecutionStatus.OutcomeUnknown;
        FinishedAt = NormalizeFinishedAt(finishedAt);
        ProcessedRows = null;
        ErrorMessage = null;
    }

    /// <summary>人工核验或原执行 owner 的明确 COMMIT ACK 将未知态收敛为成功。</summary>
    public void ResolveAsSucceeded(DateTime resolvedAt, long? processedRows = null)
    {
        EnsureStatus(ExecutionStatus.OutcomeUnknown);
        EnsureProcessedRows(processedRows);
        Status = ExecutionStatus.Succeeded;
        FinishedAt ??= NormalizeFinishedAt(resolvedAt);
        ProcessedRows = processedRows;
        ErrorMessage = null;
    }

    /// <summary>人工核验确认目标事务未应用后，将未知态收敛为失败。</summary>
    public void ResolveAsFailed(DateTime resolvedAt)
    {
        EnsureStatus(ExecutionStatus.OutcomeUnknown);
        Status = ExecutionStatus.Failed;
        FinishedAt ??= NormalizeFinishedAt(resolvedAt);
        ProcessedRows = null;
        ErrorMessage = null;
    }

    private void EnsureStatus(ExecutionStatus expected)
    {
        if (Status == expected)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Pipeline execution {Id} cannot transition from {Status}; expected {expected}.");
    }

    private static void EnsureProcessedRows(long? processedRows)
    {
        if (processedRows < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processedRows),
                "Processed rows cannot be negative.");
        }
    }

    private DateTime NormalizeFinishedAt(DateTime finishedAt)
    {
        return finishedAt < StartedAt ? StartedAt : finishedAt;
    }
}
