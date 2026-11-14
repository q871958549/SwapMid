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
public class NotificationRecipientAppService
    : CrudAppService<NotificationRecipient, NotificationRecipientDto, Guid, PagedAndSortedResultRequestDto, CreateUpdateNotificationRecipientDto>,
      INotificationRecipientAppService
{
    public NotificationRecipientAppService(IRepository<NotificationRecipient, Guid> repository)
        : base(repository)
    {
    }

    private static readonly IReadOnlyDictionary<string, Func<IQueryable<NotificationRecipient>, bool, IOrderedQueryable<NotificationRecipient>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<NotificationRecipient>, bool, IOrderedQueryable<NotificationRecipient>>>
        {
            ["name"] = (q, d) => d ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name),
            ["email"] = (q, d) => d ? q.OrderByDescending(x => x.Email) : q.OrderBy(x => x.Email),
            ["minlevel"] = (q, d) => d ? q.OrderByDescending(x => x.MinLevel) : q.OrderBy(x => x.MinLevel),
            ["isenabled"] = (q, d) => d ? q.OrderByDescending(x => x.IsEnabled) : q.OrderBy(x => x.IsEnabled),
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
            ["lastmodificationtime"] = (q, d) => d ? q.OrderByDescending(x => x.LastModificationTime) : q.OrderBy(x => x.LastModificationTime),
        };

    protected override IQueryable<NotificationRecipient> ApplySorting(
        IQueryable<NotificationRecipient> query,
        PagedAndSortedResultRequestDto input)
        => query.ApplyWhitelistedSorting(input.Sorting, SortWhitelist, "name", logger: Logger);

    protected override NotificationRecipient MapToEntity(CreateUpdateNotificationRecipientDto input)
        => new(GuidGenerator.Create(), input.Name, input.Email, input.WebhookTarget, input.MinLevel, input.Sources, input.IsEnabled);

    protected override void MapToEntity(CreateUpdateNotificationRecipientDto input, NotificationRecipient entity)
        => entity.Update(input.Name, input.Email, input.WebhookTarget, input.MinLevel, input.Sources, input.IsEnabled);

    protected override NotificationRecipientDto MapToGetOutputDto(NotificationRecipient entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Email = entity.Email,
        WebhookTarget = entity.WebhookTarget,
        MinLevel = entity.MinLevel,
        Sources = entity.Sources,
        IsEnabled = entity.IsEnabled,
        CreationTime = entity.CreationTime,
        CreatorId = entity.CreatorId,
        LastModificationTime = entity.LastModificationTime,
        LastModifierId = entity.LastModifierId
    };

    protected override NotificationRecipientDto MapToGetListOutputDto(NotificationRecipient entity)
        => MapToGetOutputDto(entity);

    public Task<List<string>> GetSourcesAsync()
        => Task.FromResult(NotificationSources.All.ToList());
}
