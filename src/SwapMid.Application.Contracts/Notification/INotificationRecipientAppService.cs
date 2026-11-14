using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SwapMid.Notification;

public interface INotificationRecipientAppService
    : ICrudAppService<NotificationRecipientDto, Guid, PagedAndSortedResultRequestDto, CreateUpdateNotificationRecipientDto>
{
    Task<List<string>> GetSourcesAsync();
}

public class NotificationRecipientDto : FullAuditedEntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? WebhookTarget { get; set; }
    public NotificationLevel MinLevel { get; set; }
    public string? Sources { get; set; }
    public bool IsEnabled { get; set; }
}

public class CreateUpdateNotificationRecipientDto
{
    [Required]
    [StringLength(64)]
    public string Name { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; set; }

    [StringLength(128)]
    public string? WebhookTarget { get; set; }

    public NotificationLevel MinLevel { get; set; } = NotificationLevel.Info;

    [StringLength(256)]
    public string? Sources { get; set; }

    public bool IsEnabled { get; set; } = true;
}
