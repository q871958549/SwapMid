using System;

namespace SwapMid.DataIntegration;

/// <summary>
/// 目标事务已开始提交，但客户端未收到可证明的最终结果。固定低敏类型，不携带 provider
/// 异常消息或 inner exception；调用方必须持久化 OutcomeUnknown 并禁止自动重试。
/// </summary>
public sealed class EtlTargetCommitOutcomeUnknownException : Exception
{
    public EtlTargetReceiptEvidence? ReceiptEvidence { get; }

    public EtlTargetCommitOutcomeUnknownException(
        EtlTargetReceiptEvidence? receiptEvidence = null)
        : base("ETL target commit outcome is unknown.")
    {
        ReceiptEvidence = receiptEvidence;
    }
}
