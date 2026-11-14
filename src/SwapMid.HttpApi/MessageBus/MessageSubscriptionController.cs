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
    [HttpGet] public Task<PagedResultDto<MessageSubscriptionDto>> GetListAsync(GetMessageSubscriptionListInput input) => _service.GetListAsync(input);
    [HttpGet("{id}")] public Task<MessageSubscriptionDto> GetAsync(Guid id) => _service.GetAsync(id);
    [HttpPost] public Task<MessageSubscriptionDto> CreateAsync(MessageSubscriptionCreateUpdateDto input) => _service.CreateAsync(input);
    [HttpPut("{id}")] public Task<MessageSubscriptionDto> UpdateAsync(Guid id, MessageSubscriptionCreateUpdateDto input) => _service.UpdateAsync(id, input);
    [HttpDelete("{id}")] public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);
}
