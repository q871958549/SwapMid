using System;

namespace SwapMid.DataIntegration;

/// <summary>
/// 从平台 Running 声明派生的不可变执行身份。只包含 ID、协议值与摘要，不含连接串、
/// SQL 正文或凭据；目标 writer 必须使用此上下文写入同事务回执。
/// </summary>
public sealed record EtlExecutionContext(
    Guid RunId,
    Guid PipelineId,
    string LegacyTargetResourceHash,
    string? VerifiedTargetResourceHash,
    string? ExecutionContractHash,
    string? TargetCoordinationIdentityHash,
    string SourceQueryHash,
    string CanonicalTargetTable,
    EtlWriteMode WriteMode,
    EtlTargetReceiptContext? TargetReceipt)
{
    public short EvidenceProtocol => TargetReceipt == null ? (short)0 : (short)1;
}

/// <summary>由目标控制表证明的 receipt-v1 物理资源身份。</summary>
public sealed record EtlTargetReceiptContext(Guid ResourceInstanceId);

/// <summary>
/// 目标事务准备并写入的低敏回执证据。该对象不携带 provider 异常、SQL 或连接信息。
/// </summary>
public sealed record EtlTargetReceiptEvidence(
    Guid RunId,
    Guid PipelineId,
    Guid ResourceInstanceId,
    string LegacyTargetResourceHash,
    string VerifiedTargetResourceHash,
    string ExecutionContractHash,
    string SourceQueryHash,
    string TargetSchemaHash,
    string BatchDigest,
    EtlWriteMode WriteMode,
    long ProcessedRows);
