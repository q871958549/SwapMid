using System;
using System.Threading.Tasks;
using SwapMid.SystemManagement;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;

namespace SwapMid.Controllers.SystemManagement;

[RemoteService]
[Area("app")]
[Route("api/v1/data-dictionaries")]
public class DataDictionaryController : SwapMidController, IDataDictionaryAppService
{
    private readonly IDataDictionaryAppService _service;

    public DataDictionaryController(IDataDictionaryAppService service)
    {
        _service = service;
    }

    // ===== 字典类型 =====

    [HttpGet]
    public Task<PagedResultDto<DataDictionaryDto>> GetListAsync(GetDataDictionaryListInput input)
        => _service.GetListAsync(input);

    [HttpGet("{id:guid}")]
    public Task<DataDictionaryDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    [HttpPost]
    public Task<DataDictionaryDto> CreateAsync(DataDictionaryCreateUpdateDto input)
        => _service.CreateAsync(input);

    [HttpPut("{id:guid}")]
    public Task<DataDictionaryDto> UpdateAsync(Guid id, DataDictionaryCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    [HttpDelete("{id:guid}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    // ===== 字典项 =====

    [HttpGet("{dictionaryId:guid}/items")]
    public Task<ListResultDto<DataDictionaryItemDto>> GetItemsAsync(Guid dictionaryId)
        => _service.GetItemsAsync(dictionaryId);

    [HttpPost("items")]
    public Task<DataDictionaryItemDto> CreateItemAsync(DataDictionaryItemCreateUpdateDto input)
        => _service.CreateItemAsync(input);

    [HttpPut("items/{itemId:guid}")]
    public Task<DataDictionaryItemDto> UpdateItemAsync(Guid itemId, DataDictionaryItemCreateUpdateDto input)
        => _service.UpdateItemAsync(itemId, input);

    [HttpDelete("items/{itemId:guid}")]
    public Task DeleteItemAsync(Guid itemId)
        => _service.DeleteItemAsync(itemId);

    // ===== 消费接口 =====

    [HttpGet("by-code/{code}/items")]
    public Task<ListResultDto<DataDictionaryItemDto>> GetItemsByCodeAsync(string code)
        => _service.GetItemsByCodeAsync(code);
}
