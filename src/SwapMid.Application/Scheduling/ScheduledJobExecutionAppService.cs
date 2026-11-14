using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.Scheduling;

/// <summary>定时任务执行历史查询实现（只读；权限复用「定时任务」查看权限）。</summary>
[Authorize(SwapMidPermissions.SystemManagement.ScheduledJobs.Default)]
public class ScheduledJobExecutionAppService : SwapMidAppService, IScheduledJobExecutionAppService
{
    private readonly IRepository<ScheduledJobExecution, Guid> _repository;

    public ScheduledJobExecutionAppService(IRepository<ScheduledJobExecution, Guid> repository)
    {
        _repository = repository;
    }

    public async Task<PagedResultDto<ScheduledJobExecutionDto>> GetListAsync(GetScheduledJobExecutionListInput input)
    {
        var queryable = await _repository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.JobKey))
        {
            queryable = queryable.Where(x => x.JobKey == input.JobKey);
        }

        if (input.Status.HasValue)
        {
            queryable = queryable.Where(x => x.Status == input.Status.Value);
        }

        if (input.TriggerKind.HasValue)
        {
            queryable = queryable.Where(x => x.TriggerKind == input.TriggerKind.Value);
        }

        if (input.IsDryRun.HasValue)
        {
            queryable = queryable.Where(x => x.IsDryRun == input.IsDryRun.Value);
        }

        if (input.StartedFrom.HasValue)
        {
            queryable = queryable.Where(x => x.StartedAt >= input.StartedFrom.Value);
        }

        if (input.StartedTo.HasValue)
        {
            queryable = queryable.Where(x => x.StartedAt <= input.StartedTo.Value);
        }

        var totalCount = await AsyncExecuter.CountAsync(queryable);

        var items = await AsyncExecuter.ToListAsync(
            // ⚠️ defaultDescending: true 不可省（原为 OrderByDescending(StartedAt)）。
            queryable
                .ApplyWhitelistedSorting(input.Sorting, SortWhitelist, "startedat", defaultDescending: true, logger: Logger)
                .Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<ScheduledJobExecutionDto>(totalCount, items.Select(ToDto).ToList());
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）。此前硬编码 OrderByDescending(StartedAt) 完全忽略
    // input.Sorting——前端执行历史弹窗（execution-history-dialog.tsx）甚至已显式传 'StartedAt desc'，
    // 一直被无视。默认序保持开始时间倒序不变。
    // Error / Summary / ArgsJson 是明细文本载荷，不作为排序维度。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<ScheduledJobExecution>, bool, IOrderedQueryable<ScheduledJobExecution>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<ScheduledJobExecution>, bool, IOrderedQueryable<ScheduledJobExecution>>>
        {
            ["startedat"] = (q, d) => d ? q.OrderByDescending(x => x.StartedAt) : q.OrderBy(x => x.StartedAt),
            ["finishedat"] = (q, d) => d ? q.OrderByDescending(x => x.FinishedAt) : q.OrderBy(x => x.FinishedAt),
            ["durationms"] = (q, d) => d ? q.OrderByDescending(x => x.DurationMs) : q.OrderBy(x => x.DurationMs),
            ["jobkey"] = (q, d) => d ? q.OrderByDescending(x => x.JobKey) : q.OrderBy(x => x.JobKey),
            ["jobtype"] = (q, d) => d ? q.OrderByDescending(x => x.JobType) : q.OrderBy(x => x.JobType),
            ["triggerkind"] = (q, d) => d ? q.OrderByDescending(x => x.TriggerKind) : q.OrderBy(x => x.TriggerKind),
            ["status"] = (q, d) => d ? q.OrderByDescending(x => x.Status) : q.OrderBy(x => x.Status),
            ["isdryrun"] = (q, d) => d ? q.OrderByDescending(x => x.IsDryRun) : q.OrderBy(x => x.IsDryRun),
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
        };

    private static ScheduledJobExecutionDto ToDto(ScheduledJobExecution e) => new()
    {
        Id = e.Id,
        JobKey = e.JobKey,
        JobType = e.JobType,
        JobMethod = e.JobMethod,
        TriggerKind = e.TriggerKind,
        Status = e.Status,
        StartedAt = e.StartedAt,
        FinishedAt = e.FinishedAt,
        DurationMs = e.DurationMs,
        Summary = e.Summary,
        Error = e.Error,
        IsDryRun = e.IsDryRun,
        ArgsJson = e.ArgsJson,
        CreatorId = e.CreatorId,
    };
}
