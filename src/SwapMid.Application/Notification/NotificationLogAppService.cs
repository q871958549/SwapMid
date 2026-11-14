using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.Notification;

[Authorize(SwapMidPermissions.NotificationCenter.Settings.Default)]
public class NotificationLogAppService : ApplicationService, INotificationLogAppService
{
    private readonly IRepository<NotificationLog, Guid> _repository;

    public NotificationLogAppService(IRepository<NotificationLog, Guid> repository)
    {
        _repository = repository;
    }

    public async Task<PagedResultDto<NotificationLogDto>> GetListAsync(GetNotificationLogListInput input)
    {
        var queryable = await _repository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Source))
            queryable = queryable.Where(x => x.Source == input.Source);
        if (input.Success.HasValue)
            queryable = queryable.Where(x => x.Success == input.Success.Value);
        if (input.Level.HasValue)
            queryable = queryable.Where(x => x.Level == input.Level.Value);

        var totalCount = await AsyncExecuter.CountAsync(queryable);

        // 台账默认按发送时间倒序（最新在前）
        var items = await AsyncExecuter.ToListAsync(
            // ⚠️ defaultDescending: true 不可省（原为 OrderByDescending(CreationTime)）。
            queryable
                .ApplyWhitelistedSorting(input.Sorting, SortWhitelist, "creationtime", defaultDescending: true, logger: Logger)
                .PageBy(input.SkipCount, input.MaxResultCount));

        return new PagedResultDto<NotificationLogDto>(totalCount, items.Select(ToDto).ToList());
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）。此前硬编码 OrderByDescending(CreationTime)
    // 完全忽略 input.Sorting。默认序保持发送时间倒序（最新在前）不变。
    // Error 是失败详情文本，不作为排序维度。NotificationLog 是 CreationAuditedEntity，无修改审计字段。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<NotificationLog>, bool, IOrderedQueryable<NotificationLog>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<NotificationLog>, bool, IOrderedQueryable<NotificationLog>>>
        {
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
            ["channel"] = (q, d) => d ? q.OrderByDescending(x => x.Channel) : q.OrderBy(x => x.Channel),
            ["title"] = (q, d) => d ? q.OrderByDescending(x => x.Title) : q.OrderBy(x => x.Title),
            ["touser"] = (q, d) => d ? q.OrderByDescending(x => x.ToUser) : q.OrderBy(x => x.ToUser),
            ["level"] = (q, d) => d ? q.OrderByDescending(x => x.Level) : q.OrderBy(x => x.Level),
            ["source"] = (q, d) => d ? q.OrderByDescending(x => x.Source) : q.OrderBy(x => x.Source),
            ["success"] = (q, d) => d ? q.OrderByDescending(x => x.Success) : q.OrderBy(x => x.Success),
        };

    private static NotificationLogDto ToDto(NotificationLog log) => new()
    {
        Id = log.Id,
        Channel = log.Channel,
        Title = log.Title,
        ToUser = log.ToUser,
        Level = log.Level,
        Source = log.Source,
        Success = log.Success,
        Error = log.Error,
        CreationTime = log.CreationTime,
        CreatorId = log.CreatorId,
    };
}
