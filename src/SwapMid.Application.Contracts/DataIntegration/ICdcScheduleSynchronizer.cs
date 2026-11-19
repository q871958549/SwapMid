using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.DataIntegration;

/// <summary>
/// CDC 流定时 WAL 增量捕获后台任务同步器。
/// </summary>
public interface ICdcScheduleSynchronizer
{
    Task SyncAsync(
        Guid streamId,
        CdcStreamStatus status,
        int pollIntervalSeconds,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid streamId,
        CancellationToken cancellationToken = default);

    Task SyncMaintenanceAsync(
        Guid streamId,
        CdcStreamStatus status,
        string? maintenanceCron,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public sealed class NullCdcScheduleSynchronizer : ICdcScheduleSynchronizer
{
    public static readonly NullCdcScheduleSynchronizer Instance = new();

    public Task SyncAsync(
        Guid streamId,
        CdcStreamStatus status,
        int pollIntervalSeconds,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveAsync(
        Guid streamId,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
