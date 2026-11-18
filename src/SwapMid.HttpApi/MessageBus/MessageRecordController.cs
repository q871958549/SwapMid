using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwapMid.MessageBus;
using SwapMid.SharedKernel;
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
    /// <summary>分页查询消息总线事件投递记录。</summary>
    [HttpGet] public Task<PagedResultDto<MessageRecordDto>> GetListAsync(GetMessageRecordListInput input) => _service.GetListAsync(input);
    /// <summary>获取单条消息记录详情。</summary>
    [HttpGet("{id}")] public Task<MessageRecordDto> GetAsync(Guid id) => _service.GetAsync(id);
    /// <summary>查看指定消息历次下游投递尝试明细（状态码、耗时与失败摘要）。</summary>
    [HttpGet("{id}/attempts")] public Task<List<MessageDeliveryAttemptDto>> GetAttemptsAsync(Guid id) => _service.GetAttemptsAsync(id);
    /// <summary>向指定消息主题手动发布 CloudEvents 消息。</summary>
    [HttpPost("publish")] public Task<MessageRecordDto> PublishAsync(PublishMessageDto input) => _service.PublishAsync(input);
    /// <summary>手动重放投递单条消息记录。</summary>
    [HttpPut("{id}/replay")] public Task ReplayAsync(Guid id) => _service.ReplayAsync(id);
    /// <summary>按 Id 集合批量重投未成功消息记录</summary>
    [HttpPost("batch-replay")]
    public Task<BatchOperationResultDto> BatchReplayAsync(BatchIdsInput input) => _service.BatchReplayAsync(input);
}
