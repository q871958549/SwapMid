using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace SwapMid.HttpApiHost.DataIntegration;

/// <summary>
/// 事务性 Outbox 后台作业中继器：当宿主启用 <c>AbpBackgroundJobsHangfireModule</c> 时，
/// ABP 默认不会启动针对 <see cref="IBackgroundJobStore"/>（<c>AbpBackgroundJobs</c> 表）的轮询器。
/// 本服务在分布式互斥锁保护下定期消费与业务实体同事务提交的 Outbox 作业（如 ETL 终态事件与失败告警）。
/// </summary>
public sealed class TransactionalOutboxRelayHostedService : BackgroundService
{
    public const string DistributedLockKey = "swapmid:outbox:abp-background-jobs-relay";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly ILogger<TransactionalOutboxRelayHostedService> _logger;

    public TransactionalOutboxRelayHostedService(
        IServiceScopeFactory scopeFactory,
        IAbpDistributedLock distributedLock,
        ILogger<TransactionalOutboxRelayHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _distributedLock = distributedLock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainPendingJobsOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Transactional outbox relay poll iteration failed exceptionType={ExceptionType}",
                    ex.GetType().FullName ?? ex.GetType().Name);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task DrainPendingJobsOnceAsync(CancellationToken cancellationToken)
    {
        await using var lockHandle = await _distributedLock.TryAcquireAsync(
            DistributedLockKey,
            TimeSpan.Zero,
            cancellationToken);
        if (lockHandle is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var jobOptions = scope.ServiceProvider.GetRequiredService<IOptions<AbpBackgroundJobOptions>>().Value;
        if (!jobOptions.IsJobExecutionEnabled)
        {
            return;
        }

        var workerOptions = scope.ServiceProvider.GetRequiredService<IOptions<AbpBackgroundJobWorkerOptions>>().Value;
        var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var store = scope.ServiceProvider.GetRequiredService<IBackgroundJobStore>();
        var serializer = scope.ServiceProvider.GetRequiredService<IBackgroundJobSerializer>();
        var executer = scope.ServiceProvider.GetRequiredService<IBackgroundJobExecuter>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        System.Collections.Generic.List<BackgroundJobInfo> waitingJobs;
        using (var readUow = uowManager.Begin(requiresNew: true, isTransactional: false))
        {
            waitingJobs = await store.GetWaitingJobsAsync(
                workerOptions.ApplicationName,
                workerOptions.MaxJobFetchCount);
            await readUow.CompleteAsync(cancellationToken);
        }

        if (waitingJobs.Count == 0)
        {
            return;
        }

        foreach (var jobInfo in waitingJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            jobInfo.TryCount++;
            jobInfo.LastTryTime = clock.Now;

            try
            {
                var config = jobOptions.GetJob(jobInfo.JobName);
                var jobArgs = serializer.Deserialize(jobInfo.JobArgs, config.ArgsType);
                using var jobScope = _scopeFactory.CreateScope();
                await executer.ExecuteAsync(new JobExecutionContext(
                    jobScope.ServiceProvider,
                    config.JobType,
                    jobArgs,
                    cancellationToken));

                using var deleteUow = uowManager.Begin(requiresNew: true, isTransactional: true);
                await store.DeleteAsync(jobInfo.Id);
                await deleteUow.CompleteAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var nextTry = CalculateNextTryTime(jobInfo, workerOptions, clock);
                if (nextTry.HasValue)
                {
                    jobInfo.NextTryTime = nextTry.Value;
                }
                else
                {
                    jobInfo.IsAbandoned = true;
                }

                await SafeUpdateJobAsync(uowManager, store, jobInfo);
                _logger.LogWarning(
                    "Transactional outbox job failed jobId={JobId} jobName={JobName} tryCount={TryCount} abandoned={Abandoned} exceptionType={ExceptionType}",
                    jobInfo.Id,
                    jobInfo.JobName,
                    jobInfo.TryCount,
                    jobInfo.IsAbandoned,
                    ex.GetType().FullName ?? ex.GetType().Name);
            }
        }
    }

    private static DateTime? CalculateNextTryTime(
        BackgroundJobInfo jobInfo,
        AbpBackgroundJobWorkerOptions workerOptions,
        IClock clock)
    {
        var nextWaitSeconds = workerOptions.DefaultFirstWaitDuration *
                              Math.Pow(workerOptions.DefaultWaitFactor, jobInfo.TryCount - 1);
        var nextTry = jobInfo.LastTryTime?.AddSeconds(nextWaitSeconds) ??
                      clock.Now.AddSeconds(nextWaitSeconds);

        if (nextTry.Subtract(jobInfo.CreationTime).TotalSeconds > workerOptions.DefaultTimeout)
        {
            return null;
        }

        return nextTry;
    }

    private async Task SafeUpdateJobAsync(
        IUnitOfWorkManager uowManager,
        IBackgroundJobStore store,
        BackgroundJobInfo jobInfo)
    {
        try
        {
            using var updateUow = uowManager.Begin(requiresNew: true, isTransactional: true);
            await store.UpdateAsync(jobInfo);
            await updateUow.CompleteAsync();
        }
        catch (Exception updateEx)
        {
            _logger.LogError(
                "Failed to update transactional outbox job state jobId={JobId} exceptionType={ExceptionType}",
                jobInfo.Id,
                updateEx.GetType().FullName ?? updateEx.GetType().Name);
        }
    }
}
