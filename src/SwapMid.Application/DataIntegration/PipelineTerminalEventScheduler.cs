using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace SwapMid.DataIntegration;

/// <summary>
/// ETL 终态事件事务性 Outbox 调度端口：在终态提交的同一数据库事务内写入 ABP 后台作业表。
/// </summary>
public interface IPipelineTerminalEventScheduler
{
    Task ScheduleTerminalEventAsync(
        PipelineExecution execution,
        PipelineExecutionFailurePolicy.FailureDescriptor? failure);
}

/// <summary>
/// ETL 终态集成事件 Outbox 调度器：将安全脱敏后的终态快照写入 <see cref="IBackgroundJobStore"/>，
/// 与 <see cref="PipelineExecution"/> 终态变更同事务提交或回滚，且不占用协调器锁等待下游网络。
/// </summary>
[ExposeServices(
    typeof(IPipelineTerminalEventScheduler),
    typeof(PipelineTerminalEventScheduler))]
public sealed class PipelineTerminalEventScheduler :
    IPipelineTerminalEventScheduler,
    ITransientDependency
{
    private readonly IBackgroundJobStore _backgroundJobStore;
    private readonly IBackgroundJobSerializer _backgroundJobSerializer;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;
    private readonly AbpBackgroundJobWorkerOptions _workerOptions;
    private readonly IPipelineTerminalUnitOfWorkGuard
        _terminalUnitOfWorkGuard;
    private readonly IPipelineTerminalStoreTopologyGuard
        _terminalStoreTopologyGuard;

    public PipelineTerminalEventScheduler(
        IBackgroundJobStore backgroundJobStore,
        IBackgroundJobSerializer backgroundJobSerializer,
        IGuidGenerator guidGenerator,
        IClock clock,
        IOptions<AbpBackgroundJobWorkerOptions> workerOptions,
        IPipelineTerminalUnitOfWorkGuard terminalUnitOfWorkGuard,
        IPipelineTerminalStoreTopologyGuard terminalStoreTopologyGuard)
    {
        _backgroundJobStore = backgroundJobStore;
        _backgroundJobSerializer = backgroundJobSerializer;
        _guidGenerator = guidGenerator;
        _clock = clock;
        _workerOptions = workerOptions.Value;
        _terminalUnitOfWorkGuard = terminalUnitOfWorkGuard;
        _terminalStoreTopologyGuard = terminalStoreTopologyGuard;
    }

    public async Task ScheduleTerminalEventAsync(
        PipelineExecution execution,
        PipelineExecutionFailurePolicy.FailureDescriptor? failure)
    {
        _terminalUnitOfWorkGuard.EnsureTransactional();

        _terminalStoreTopologyGuard.EnsureSafeScope(
            PipelineTerminalStoreTopologyPhase.NotificationSchedule);

        var args = new PipelineTerminalEventJobArgs
        {
            ExecutionId = execution.Id,
            PipelineId = execution.PipelineId,
            PipelineName = execution.PipelineName,
            TriggerType = execution.TriggerType,
            Status = execution.Status,
            ProcessedRows = execution.ProcessedRows,
            StartedAtUtc = execution.StartedAt,
            CompletedAtUtc = execution.FinishedAt,
            ErrorCode = failure?.ErrorCode,
            CorrelationId = failure?.CorrelationId,
        };

        var now = _clock.Now;
        await _backgroundJobStore.InsertAsync(
            new BackgroundJobInfo
            {
                Id = _guidGenerator.Create(),
                ApplicationName = _workerOptions.ApplicationName,
                JobName = BackgroundJobNameAttribute.GetName<
                    PipelineTerminalEventJobArgs>(),
                JobArgs = _backgroundJobSerializer.Serialize(args),
                Priority = BackgroundJobPriority.Normal,
                CreationTime = now,
                NextTryTime = now,
            });
    }
}
