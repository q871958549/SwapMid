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

    /// <summary>分页查询通知接收人配置列表。</summary>
    [HttpGet]
    public Task<PagedResultDto<NotificationRecipientDto>> GetListAsync(PagedAndSortedResultRequestDto input) => _service.GetListAsync(input);

    /// <summary>全部已知通知来源（代码常量为事实源）：「订阅来源」下拉选项，替代手输防错配。</summary>
    [HttpGet("sources")]
    public Task<List<string>> GetSourcesAsync() => _service.GetSourcesAsync();

    /// <summary>获取单个通知接收人详情。</summary>
    [HttpGet("{id}")]
    public Task<NotificationRecipientDto> GetAsync(Guid id) => _service.GetAsync(id);

    /// <summary>创建通知接收人订阅规则。</summary>
    [HttpPost]
    public Task<NotificationRecipientDto> CreateAsync(CreateUpdateNotificationRecipientDto input) => _service.CreateAsync(input);

    /// <summary>更新通知接收人联系方式及订阅来源规则。</summary>
    [HttpPut("{id}")]
    public Task<NotificationRecipientDto> UpdateAsync(Guid id, CreateUpdateNotificationRecipientDto input) => _service.UpdateAsync(id, input);

    /// <summary>删除指定通知接收人。</summary>
    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);
}
