using System;
using System.Threading.Tasks;
using SwapMid.MessageBus;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;

namespace SwapMid.Controllers.MessageBus;

[RemoteService][Area("app")][Route("api/v1/message-subscriptions")]
public class MessageSubscriptionController : SwapMidController, IMessageSubscriptionAppService
{
    private readonly IMessageSubscriptionAppService _service;
    public MessageSubscriptionController(IMessageSubscriptionAppService service) { _service = service; }
    /// <summary>分页查询消息订阅与投递渠道配置列表。</summary>
    [HttpGet] public Task<PagedResultDto<MessageSubscriptionDto>> GetListAsync(GetMessageSubscriptionListInput input) => _service.GetListAsync(input);
    /// <summary>获取单条消息订阅详情。</summary>
    [HttpGet("{id}")] public Task<MessageSubscriptionDto> GetAsync(Guid id) => _service.GetAsync(id);
    /// <summary>创建消息订阅（支持 HTTP Webhook、RabbitMQ 与 BotWebhook 渠道）。</summary>
    [HttpPost] public Task<MessageSubscriptionDto> CreateAsync(MessageSubscriptionCreateUpdateDto input) => _service.CreateAsync(input);
    /// <summary>更新消息订阅目标、过滤规则与重试配置。</summary>
    [HttpPut("{id}")] public Task<MessageSubscriptionDto> UpdateAsync(Guid id, MessageSubscriptionCreateUpdateDto input) => _service.UpdateAsync(id, input);
    /// <summary>删除指定消息订阅。</summary>
    [HttpDelete("{id}")] public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);
}
