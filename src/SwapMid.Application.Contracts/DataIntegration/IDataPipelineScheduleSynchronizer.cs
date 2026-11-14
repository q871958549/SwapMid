using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.DataIntegration;

/// <summary>
/// 数据管道 Cron 定时调度同步器契约。
/// 仅当管道处于 <see cref="PipelineStatus.Active"/> 且启用定时调度时注册周期任务，
/// 其余状态（草稿、暂停、删除或关闭定时）一律移除周期任务。
/// </summary>
public interface IDataPipelineScheduleSynchronizer
{
    Task SyncAsync(
        Guid pipelineId,
        PipelineStatus status,
        bool scheduleEnabled,
        string? scheduleCron,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid pipelineId,
        CancellationToken cancellationToken = default);
}
