using System.Threading.Tasks;
using SwapMid.Notification;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;

namespace SwapMid.Controllers.Notification;

[RemoteService]
[Area("app")]
[Route("api/v1/notification/logs")]
public class NotificationLogController : SwapMidController, INotificationLogAppService
{
    private readonly INotificationLogAppService _service;

    public NotificationLogController(INotificationLogAppService service)
    {
        _service = service;
    }

    /// <summary>分页查询通知发送流水日志。</summary>
    [HttpGet]
    public Task<PagedResultDto<NotificationLogDto>> GetListAsync(GetNotificationLogListInput input)
        => _service.GetListAsync(input);
}
