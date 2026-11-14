using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace SwapMid.DataIntegration;

/// <summary>
/// ETL 失败通知调度器：只允许在终态事务内把安全参数耐久写入 ABP 后台队列。
/// 网络发送由独立后台任务完成，禁止通过 UoW OnCompleted 回调占用协调器 fence。
/// </summary>
public sealed class PipelineFailureNotificationScheduler : ITransientDependency
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

    public PipelineFailureNotificationScheduler(
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

    public async Task ScheduleAsync(
        Guid pipelineId,
        Guid executionId,
        PipelineExecutionFailurePolicy.FailureDescriptor failure,
        bool outcomeUnknown = false)
    {
        _terminalUnitOfWorkGuard.EnsureTransactional();

        _terminalStoreTopologyGuard.EnsureSafeScope(
            PipelineTerminalStoreTopologyPhase.NotificationSchedule);

        var args = new PipelineFailureNotificationJobArgs
        {
            PipelineId = pipelineId,
            ExecutionId = executionId,
            ErrorCode = failure.ErrorCode,
            CorrelationId = failure.CorrelationId,
            ExceptionType = failure.ExceptionType,
            OutcomeUnknown = outcomeUnknown,
        };
        var now = _clock.Now;
        await _backgroundJobStore.InsertAsync(
            new BackgroundJobInfo
            {
                Id = _guidGenerator.Create(),
                ApplicationName = _workerOptions.ApplicationName,
                JobName = BackgroundJobNameAttribute.GetName<
                    PipelineFailureNotificationJobArgs>(),
                JobArgs = _backgroundJobSerializer.Serialize(args),
                Priority = BackgroundJobPriority.Normal,
                CreationTime = now,
                NextTryTime = now,
            });
    }
}
