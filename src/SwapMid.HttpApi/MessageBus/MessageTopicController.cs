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
    /// <summary>分页查询消息总线主题列表。</summary>
    [HttpGet] public Task<PagedResultDto<MessageTopicDto>> GetListAsync(GetMessageTopicListInput input) => _service.GetListAsync(input);
    /// <summary>获取单个消息主题详情。</summary>
    [HttpGet("{id}")] public Task<MessageTopicDto> GetAsync(Guid id) => _service.GetAsync(id);
    /// <summary>创建新消息主题。</summary>
    [HttpPost] public Task<MessageTopicDto> CreateAsync(MessageTopicCreateUpdateDto input) => _service.CreateAsync(input);
    /// <summary>更新消息主题描述与配置。</summary>
    [HttpPut("{id}")] public Task<MessageTopicDto> UpdateAsync(Guid id, MessageTopicCreateUpdateDto input) => _service.UpdateAsync(id, input);
    /// <summary>删除指定消息主题。</summary>
    [HttpDelete("{id}")] public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);
}
