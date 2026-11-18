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

    /// <summary>分页查询数据字典类型定义列表。</summary>
    [HttpGet]
    public Task<PagedResultDto<DataDictionaryDto>> GetListAsync(GetDataDictionaryListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单个数据字典类型详情。</summary>
    [HttpGet("{id:guid}")]
    public Task<DataDictionaryDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    /// <summary>创建数据字典类型定义。</summary>
    [HttpPost]
    public Task<DataDictionaryDto> CreateAsync(DataDictionaryCreateUpdateDto input)
        => _service.CreateAsync(input);

    /// <summary>更新数据字典类型名称与描述。</summary>
    [HttpPut("{id:guid}")]
    public Task<DataDictionaryDto> UpdateAsync(Guid id, DataDictionaryCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>删除指定数据字典类型及所属条目。</summary>
    [HttpDelete("{id:guid}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    // ===== 字典项 =====

    /// <summary>查询指定数据字典类型下的全部条目选项。</summary>
    [HttpGet("{dictionaryId:guid}/items")]
    public Task<ListResultDto<DataDictionaryItemDto>> GetItemsAsync(Guid dictionaryId)
        => _service.GetItemsAsync(dictionaryId);

    /// <summary>新增数据字典条目。</summary>
    [HttpPost("items")]
    public Task<DataDictionaryItemDto> CreateItemAsync(DataDictionaryItemCreateUpdateDto input)
        => _service.CreateItemAsync(input);

    /// <summary>更新数据字典条目显示名、值与排序。</summary>
    [HttpPut("items/{itemId:guid}")]
    public Task<DataDictionaryItemDto> UpdateItemAsync(Guid itemId, DataDictionaryItemCreateUpdateDto input)
        => _service.UpdateItemAsync(itemId, input);

    /// <summary>删除指定数据字典条目。</summary>
    [HttpDelete("items/{itemId:guid}")]
    public Task DeleteItemAsync(Guid itemId)
        => _service.DeleteItemAsync(itemId);

    // ===== 消费接口 =====

    /// <summary>按字典唯一编码读取启用中的数据字典条目集合（供业务表单下拉消费）。</summary>
    [HttpGet("by-code/{code}/items")]
    public Task<ListResultDto<DataDictionaryItemDto>> GetItemsByCodeAsync(string code)
        => _service.GetItemsByCodeAsync(code);
}
