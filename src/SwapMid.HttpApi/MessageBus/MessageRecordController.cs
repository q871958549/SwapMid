using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwapMid.MessageBus;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;

namespace SwapMid.Controllers.MessageBus;

[RemoteService][Area("app")][Route("api/v1/message-records")]
public class MessageRecordController : SwapMidController, IMessageRecordAppService
{
    private readonly IMessageRecordAppService _service;
    public MessageRecordController(IMessageRecordAppService service) { _service = service; }
    [HttpGet] public Task<PagedResultDto<MessageRecordDto>> GetListAsync(GetMessageRecordListInput input) => _service.GetListAsync(input);
    [HttpGet("{id}")] public Task<MessageRecordDto> GetAsync(Guid id) => _service.GetAsync(id);
    [HttpGet("{id}/attempts")] public Task<List<MessageDeliveryAttemptDto>> GetAttemptsAsync(Guid id) => _service.GetAttemptsAsync(id);
    [HttpPost("publish")] public Task<MessageRecordDto> PublishAsync(PublishMessageDto input) => _service.PublishAsync(input);
    [HttpPut("{id}/replay")] public Task ReplayAsync(Guid id) => _service.ReplayAsync(id);
}
