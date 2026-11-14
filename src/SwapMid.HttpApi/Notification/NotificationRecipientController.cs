using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwapMid.Notification;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;

namespace SwapMid.Controllers.Notification;

/// <summary>通知接收人 CRUD（体系化收件人）。前端「系统管理 → 通知中心 → 接收人」用。</summary>
[RemoteService]
[Area("app")]
[Route("api/v1/notification/recipients")]
public class NotificationRecipientController : SwapMidController, INotificationRecipientAppService
{
    private readonly INotificationRecipientAppService _service;

    public NotificationRecipientController(INotificationRecipientAppService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<PagedResultDto<NotificationRecipientDto>> GetListAsync(PagedAndSortedResultRequestDto input) => _service.GetListAsync(input);

    /// <summary>全部已知通知来源（代码常量为事实源）：「订阅来源」下拉选项，替代手输防错配。</summary>
    [HttpGet("sources")]
    public Task<List<string>> GetSourcesAsync() => _service.GetSourcesAsync();

    [HttpGet("{id}")]
    public Task<NotificationRecipientDto> GetAsync(Guid id) => _service.GetAsync(id);

    [HttpPost]
    public Task<NotificationRecipientDto> CreateAsync(CreateUpdateNotificationRecipientDto input) => _service.CreateAsync(input);

    [HttpPut("{id}")]
    public Task<NotificationRecipientDto> UpdateAsync(Guid id, CreateUpdateNotificationRecipientDto input) => _service.UpdateAsync(id, input);

    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);
}
