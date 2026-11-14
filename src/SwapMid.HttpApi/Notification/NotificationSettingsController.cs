using System.Threading.Tasks;
using SwapMid.Notification;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace SwapMid.Controllers.Notification;

[RemoteService]
[Area("app")]
[Route("api/v1/notification/settings")]
public class NotificationSettingsController : SwapMidController, INotificationSettingsAppService
{
    private readonly INotificationSettingsAppService _service;

    public NotificationSettingsController(INotificationSettingsAppService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<NotificationSettingsDto> GetAsync() => _service.GetAsync();

    [HttpPut]
    public Task UpdateAsync(UpdateNotificationSettingsDto input) => _service.UpdateAsync(input);

    [HttpPost("test")]
    public Task<NotificationTestResultDto> SendTestAsync(SendTestNotificationDto input) => _service.SendTestAsync(input);

    [HttpPost("alert-token")]
    public Task<string> RegenerateAlertTokenAsync() => _service.RegenerateAlertTokenAsync();
}
