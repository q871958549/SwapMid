using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwapMid.DataIntegration;
using Volo.Abp.DependencyInjection;

namespace SwapMid.HttpApiHost.DataIntegration;

/// <summary>
/// 基于 Hangfire RecurringJob 的数据管道定时调度同步实现。
/// </summary>
[Dependency(ServiceLifetime.Transient, ReplaceServices = true)]
[ExposeServices(typeof(IDataPipelineScheduleSynchronizer))]
public sealed class HangfireDataPipelineScheduleSynchronizer :
    IDataPipelineScheduleSynchronizer
{
    public const string JobIdPrefix = "etl-pipeline:";

    private readonly IRecurringJobManager _recurringJobManager;
    private readonly ILogger<HangfireDataPipelineScheduleSynchronizer> _logger;

    public HangfireDataPipelineScheduleSynchronizer(
        IRecurringJobManager recurringJobManager,
        ILogger<HangfireDataPipelineScheduleSynchronizer> logger)
    {
        _recurringJobManager = recurringJobManager;
        _logger = logger;
    }

    public static string BuildRecurringJobId(Guid pipelineId)
        => $"{JobIdPrefix}{pipelineId:D}";

    public Task SyncAsync(
        Guid pipelineId,
        PipelineStatus status,
        bool scheduleEnabled,
        string? scheduleCron,
        CancellationToken cancellationToken = default)
    {
        var jobId = BuildRecurringJobId(pipelineId);
        if (status != PipelineStatus.Active ||
            !scheduleEnabled ||
            string.IsNullOrWhiteSpace(scheduleCron))
        {
            _recurringJobManager.RemoveIfExists(jobId);
            return Task.CompletedTask;
        }

        var normalizedCron = DataPipelineCronValidator.ValidateAndNormalize(
            scheduleEnabled: true,
            scheduleCron)!;

        _recurringJobManager.AddOrUpdate<RunScheduledDataPipelineHangfireJob>(
            jobId,
            job => job.ExecuteAsync(pipelineId, CancellationToken.None),
            normalizedCron,
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Local,
            });

        _logger.LogInformation(
            "数据管道定时调度已同步 pipelineId={PipelineId} cron={Cron}",
            pipelineId,
            normalizedCron);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(
        Guid pipelineId,
        CancellationToken cancellationToken = default)
    {
        var jobId = BuildRecurringJobId(pipelineId);
        _recurringJobManager.RemoveIfExists(jobId);
        return Task.CompletedTask;
    }
}
