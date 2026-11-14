using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

[Dependency(TryRegister = true)]
public sealed class NullDataPipelineScheduleSynchronizer :
    IDataPipelineScheduleSynchronizer,
    ISingletonDependency
{
    public static readonly NullDataPipelineScheduleSynchronizer Instance = new();

    public Task SyncAsync(
        Guid pipelineId,
        PipelineStatus status,
        bool scheduleEnabled,
        string? scheduleCron,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveAsync(
        Guid pipelineId,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
