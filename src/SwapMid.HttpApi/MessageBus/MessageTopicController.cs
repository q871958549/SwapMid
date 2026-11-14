using System;
using System.Threading.Tasks;
using SwapMid.MessageBus;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;

namespace SwapMid.Controllers.MessageBus;

[RemoteService][Area("app")][Route("api/v1/message-topics")]
public class MessageTopicController : SwapMidController, IMessageTopicAppService
{
    private readonly IMessageTopicAppService _service;
    public MessageTopicController(IMessageTopicAppService service) { _service = service; }
    [HttpGet] public Task<PagedResultDto<MessageTopicDto>> GetListAsync(GetMessageTopicListInput input) => _service.GetListAsync(input);
    [HttpGet("{id}")] public Task<MessageTopicDto> GetAsync(Guid id) => _service.GetAsync(id);
    [HttpPost] public Task<MessageTopicDto> CreateAsync(MessageTopicCreateUpdateDto input) => _service.CreateAsync(input);
    [HttpPut("{id}")] public Task<MessageTopicDto> UpdateAsync(Guid id, MessageTopicCreateUpdateDto input) => _service.UpdateAsync(id, input);
    [HttpDelete("{id}")] public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);
}
