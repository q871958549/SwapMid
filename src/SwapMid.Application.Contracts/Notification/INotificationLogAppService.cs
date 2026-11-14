using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SwapMid.Notification;

/// <summary>通知发送台账查询（只读）。</summary>
public interface INotificationLogAppService : IApplicationService
{
    Task<PagedResultDto<NotificationLogDto>> GetListAsync(GetNotificationLogListInput input);
}

public class NotificationLogDto : CreationAuditedEntityDto<Guid>
{
    public NotificationChannelType Channel { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? ToUser { get; set; }

    public NotificationLevel Level { get; set; }

    public string? Source { get; set; }

    public bool Success { get; set; }

    public string? Error { get; set; }
}

public class GetNotificationLogListInput : PagedAndSortedResultRequestDto
{
    public string? Source { get; set; }

    public bool? Success { get; set; }

    public NotificationLevel? Level { get; set; }
}
