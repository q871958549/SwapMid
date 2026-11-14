using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Hangfire;
using Hangfire.Storage;
using SwapMid.Permissions;
using SwapMid.Scheduling;
using SwapMid.Settings;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace SwapMid.HttpApiHost.Scheduling;

/// <summary>
/// 定时任务统一管控：列出代码目录中的任务并合并 Hangfire 运行态（启用/上次/下次/状态），
/// 支持启停（注册/移除 recurring job）与立即执行（统一经 <see cref="RunAsync"/> 走执行管道）。
/// </summary>
[Authorize(SwapMidPermissions.SystemManagement.ScheduledJobs.Default)]
public class JobSchedulingAppService : ApplicationService, IJobSchedulingAppService
{
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly JobStorage _jobStorage;
    private readonly ScheduledJobRunner _runner;
    private readonly IScheduledJobHandlerResolver _handlerResolver;
    private readonly ISettingManager _settingManager;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ICancellationTokenProvider _cancellationTokenProvider;
    private readonly Volo.Abp.Domain.Repositories.IRepository<ScheduledJobExecution, Guid> _executions;

    public JobSchedulingAppService(
        IRecurringJobManager recurringJobManager,
        JobStorage jobStorage,
        ScheduledJobRunner runner,
        IScheduledJobHandlerResolver handlerResolver,
        ISettingManager settingManager,
        IBackgroundJobClient backgroundJobClient,
        ICancellationTokenProvider cancellationTokenProvider,
        Volo.Abp.Domain.Repositories.IRepository<ScheduledJobExecution, Guid> executions)
    {
        _recurringJobManager = recurringJobManager;
        _jobStorage = jobStorage;
        _runner = runner;
        _handlerResolver = handlerResolver;
        _settingManager = settingManager;
        _backgroundJobClient = backgroundJobClient;
        _cancellationTokenProvider = cancellationTokenProvider;
        _executions = executions;
    }

    public async Task<List<ScheduledJobDto>> GetListAsync()
    {
        using var connection = _jobStorage.GetConnection();
        var recurring = connection.GetRecurringJobs().ToDictionary(x => x.Id);
        var pendingCron = await GetPendingCronMapAsync();
        var handlers = _handlerResolver.ResolveAvailable();

        var list = ScheduledJobCatalog.All.Select(def =>
        {
            recurring.TryGetValue(def.Key, out var rj);
            var enabled = rj != null;
            var handler = handlers.FirstOrDefault(h => h.JobKey == def.Key);
            return new ScheduledJobDto
            {
                Key = def.Key,
                DisplayName = def.DisplayName,
                Description = def.Description,
                Cron = rj?.Cron ?? (pendingCron.GetValueOrDefault(def.Key) ?? def.DefaultCron),
                Enabled = enabled,
                SupportsParameterizedRun = handler != null,
                Parameters = handler?.Parameters.ToList() ?? new List<JobParameterSchema>(),
                IsDestructive = def.IsDestructive,
                IsLongRunning = def.IsLongRunning,
                MutexGroup = handler?.MutexGroup,
                LastExecution = rj?.LastExecution,
                NextExecution = rj?.NextExecution,
                LastJobState = rj?.LastJobState,
            };
        }).ToList();

        return list;
    }

    public async Task<ScheduledJobStatsDto> GetStatsAsync()
    {
        using var connection = _jobStorage.GetConnection();
        var enabledCount = connection.GetRecurringJobs()
            .Count(rj => ScheduledJobCatalog.Find(rj.Id) != null);

        var measuredAt = Clock.Now;
        var cutoff = measuredAt.AddHours(-24);
        var queryable = await _executions.GetQueryableAsync();
        var failed24h = await AsyncExecuter.CountAsync(queryable.Where(x =>
            x.Status == JobExecutionStatus.Failed &&
            x.StartedAt >= cutoff &&
            x.StartedAt <= measuredAt));
        var running = await AsyncExecuter.CountAsync(
            queryable.Where(x => x.Status == JobExecutionStatus.Running));

        return new ScheduledJobStatsDto
        {
            EnabledCount = enabledCount,
            TotalCount = ScheduledJobCatalog.All.Count,
            Failed24hCount = failed24h,
            Failed24hStartedFrom = cutoff,
            Failed24hMeasuredAt = measuredAt,
            RunningCount = running,
        };
    }

    [Authorize(SwapMidPermissions.SystemManagement.ScheduledJobs.Manage)]
    public async Task SetEnabledAsync(SetScheduledJobEnabledInput input)
    {
        var def = ScheduledJobCatalog.Find(input.Key)
            ?? throw new UserFriendlyException("未知的定时任务：" + input.Key);

        var cron = string.IsNullOrWhiteSpace(input.Cron) ? def.DefaultCron : input.Cron.Trim();
        ParseCronOrThrow(cron);

        if (input.Enabled)
        {
            def.Register(_recurringJobManager, cron);
            await SavePendingCronAsync(def.Key, null);
        }
        else
        {
            _recurringJobManager.RemoveIfExists(def.Key);
            await SavePendingCronAsync(def.Key, cron);
        }
    }

    private static CronExpression ParseCronOrThrow(string cron)
    {
        try
        {
            return CronExpression.Parse(cron, CronFormat.Standard);
        }
        catch (CronFormatException ex)
        {
            throw new UserFriendlyException("无效的 cron 表达式：" + ex.Message);
        }
    }

    private async Task<Dictionary<string, string>> GetPendingCronMapAsync()
    {
        var json = await SettingProvider.GetOrNullAsync(SwapMidSettings.ScheduledJobsPendingCron);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private async Task SavePendingCronAsync(string jobKey, string? cron)
    {
        var map = await GetPendingCronMapAsync();
        if (cron == null)
        {
            if (!map.Remove(jobKey))
            {
                return;
            }
        }
        else
        {
            map[jobKey] = cron;
        }

        await _settingManager.SetGlobalAsync(SwapMidSettings.ScheduledJobsPendingCron, JsonSerializer.Serialize(map));
    }

    public Task<List<DateTime>> PreviewCronAsync(string cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            throw new UserFriendlyException("cron 表达式不能为空");
        }

        var expression = ParseCronOrThrow(cron.Trim());

        var results = new List<DateTime>();
        var from = DateTime.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            var next = expression.GetNextOccurrence(from, TimeZoneInfo.Local);
            if (next == null)
            {
                break;
            }

            results.Add(next.Value);
            from = next.Value;
        }

        return Task.FromResult(results);
    }

    [Authorize(SwapMidPermissions.SystemManagement.ScheduledJobs.Manage)]
    [UnitOfWork(IsDisabled = true)]
    public async Task<RunScheduledJobResultDto> RunAsync(RunScheduledJobInput input)
    {
        if (input.ArgsJson is
            { Length: > JobRunContext.MaxArgsJsonCharacters })
        {
            throw new BusinessException(
                    SwapMidDomainErrorCodes.Scheduling.JobArgumentsTooLong)
                .WithData("max", JobRunContext.MaxArgsJsonCharacters);
        }

        var hasHandler = _handlerResolver.Find(input.Key) != null;
        if (!hasHandler)
        {
            throw new UserFriendlyException("该任务未接入统一执行管道，暂不支持带参 / 演练执行：" + input.Key);
        }

        if (input.DryRun && string.IsNullOrWhiteSpace(input.TestRecipient))
        {
            throw new UserFriendlyException("演练模式需要填写测试收件人邮箱");
        }

        if (input.RunInBackground)
        {
            if (input.DryRun)
            {
                throw new UserFriendlyException("演练模式不支持后台执行（需同步等待结果）");
            }

            _backgroundJobClient.Enqueue<RunScheduledJobHangfireJob>(
                job => job.ExecuteAsync(
                    new RunScheduledJobArgs
                    {
                        JobKey = input.Key,
                        ArgsJson = string.IsNullOrWhiteSpace(input.ArgsJson)
                            ? null
                            : input.ArgsJson,
                    },
                    CancellationToken.None));

            return new RunScheduledJobResultDto
            {
                Success = true,
                Queued = true,
                Summary = "已提交后台执行，进度与结果见执行历史",
            };
        }

        var result = await _runner.RunAsync(input.Key, new JobRunContext
        {
            TriggerKind = JobTriggerKind.Manual,
            IsDryRun = input.DryRun,
            TestRecipient = input.TestRecipient?.Trim(),
            ArgsJson = string.IsNullOrWhiteSpace(input.ArgsJson) ? null : input.ArgsJson,
        }, _cancellationTokenProvider.Token);

        var success = result.Status is JobExecutionStatus.Succeeded or JobExecutionStatus.Skipped;
        return new RunScheduledJobResultDto
        {
            Success = success,
            Status = result.Status,
            Summary = result.Summary,
            Error = success ? null : result.Summary,
        };
    }
}
