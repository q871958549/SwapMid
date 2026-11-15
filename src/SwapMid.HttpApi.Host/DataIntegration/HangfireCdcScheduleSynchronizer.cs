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
/// 基于 Hangfire RecurringJob 的 CDC WAL 增量捕获后台调度同步器。
/// </summary>
[Dependency(ServiceLifetime.Transient, ReplaceServices = true)]
[ExposeServices(typeof(ICdcScheduleSynchronizer))]
public sealed class HangfireCdcScheduleSynchronizer : ICdcScheduleSynchronizer
{
    public const string JobIdPrefix = "cdc-stream:";

    private readonly IRecurringJobManager _recurringJobManager;
    private readonly ILogger<HangfireCdcScheduleSynchronizer> _logger;

    public HangfireCdcScheduleSynchronizer(
        IRecurringJobManager recurringJobManager,
        ILogger<HangfireCdcScheduleSynchronizer> logger)
    {
        _recurringJobManager = recurringJobManager;
        _logger = logger;
    }

    public static string BuildRecurringJobId(Guid streamId)
        => $"{JobIdPrefix}{streamId:D}";

    public static string BuildCronExpression(int pollIntervalSeconds)
    {
        var clamped = Math.Clamp(
            pollIntervalSeconds,
            CdcStreamConsts.MinPollIntervalSeconds,
            CdcStreamConsts.MaxPollIntervalSeconds);

        if (clamped < 60)
        {
            return $"*/{clamped} * * * * *";
        }

        var minutes = Math.Clamp(clamped / 60, 1, 59);
        return $"*/{minutes} * * * *";
    }

    public Task SyncAsync(
        Guid streamId,
        CdcStreamStatus status,
        int pollIntervalSeconds,
        CancellationToken cancellationToken = default)
    {
        var jobId = BuildRecurringJobId(streamId);
        if (status != CdcStreamStatus.Active)
        {
            _recurringJobManager.RemoveIfExists(jobId);
            return Task.CompletedTask;
        }

        var cron = BuildCronExpression(pollIntervalSeconds);
        _recurringJobManager.AddOrUpdate<RunScheduledCdcCaptureHangfireJob>(
            jobId,
            job => job.ExecuteAsync(streamId, CancellationToken.None),
            cron,
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Local,
            });

        _logger.LogInformation(
            "CDC 流后台轮询已同步 streamId={StreamId} cron={Cron}",
            streamId,
            cron);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(
        Guid streamId,
        CancellationToken cancellationToken = default)
    {
        _recurringJobManager.RemoveIfExists(BuildRecurringJobId(streamId));
        return Task.CompletedTask;
    }
}
