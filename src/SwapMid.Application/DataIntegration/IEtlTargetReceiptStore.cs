using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.DataIntegration;

public interface IEtlTargetReceiptStore
{
    Task<EtlTargetReceiptActivation> ProbeActivationAsync(
        DataSource target,
        EtlTransformConfig transform,
        CancellationToken cancellationToken);

    Task<EtlTargetReceiptEvidence?> FindAsync(
        DataSource target,
        EtlExecutionContext executionContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// 取得与正式 writer 相同的目标侧排他锁后读取回执，并把锁保持到返回句柄释放。
    /// 只用于“未应用”人工 break-glass，防止把仍在服务器端完成的旧 COMMIT 误判为 absent。
    /// </summary>
    Task<IEtlTargetReceiptReadFence> AcquireReadFenceAsync(
        DataSource target,
        EtlExecutionContext executionContext,
        CancellationToken cancellationToken);

    Task<EtlTargetReceiptInsertResult> InsertOrReadAsync(
        DbConnection connection,
        DbTransaction transaction,
        EtlTargetReceiptProviderContext providerContext,
        EtlTargetReceiptEvidence evidence,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken);
}

public interface IEtlTargetReceiptReadFence : IAsyncDisposable
{
    EtlTargetReceiptEvidence? Receipt { get; }
}

public sealed record EtlTargetReceiptActivation(
    System.Guid ResourceInstanceId,
    string Provider,
    string CanonicalTargetTable,
    string VerifiedTargetResourceHash,
    string TargetCoordinationIdentityHash);

public sealed record EtlTargetReceiptProviderContext(
    string Provider,
    string Database,
    string TargetCoordinationIdentityHash);

public enum EtlTargetReceiptInsertResult
{
    Inserted,
    ExistingExact,
    ExistingMismatch,
}
