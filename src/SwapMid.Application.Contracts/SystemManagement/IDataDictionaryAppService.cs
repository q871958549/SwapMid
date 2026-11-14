using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SwapMid.SystemManagement;

public interface IDataDictionaryAppService : IApplicationService
{
    // ===== 字典类型 =====
    Task<PagedResultDto<DataDictionaryDto>> GetListAsync(GetDataDictionaryListInput input);
    Task<DataDictionaryDto> GetAsync(Guid id);
    Task<DataDictionaryDto> CreateAsync(DataDictionaryCreateUpdateDto input);
    Task<DataDictionaryDto> UpdateAsync(Guid id, DataDictionaryCreateUpdateDto input);
    Task DeleteAsync(Guid id);

    // ===== 字典项 =====
    /// <summary>按字典 Id 获取其全部字典项（按排序号升序，含停用项，供管理端）。</summary>
    Task<ListResultDto<DataDictionaryItemDto>> GetItemsAsync(Guid dictionaryId);
    Task<DataDictionaryItemDto> CreateItemAsync(DataDictionaryItemCreateUpdateDto input);
    Task<DataDictionaryItemDto> UpdateItemAsync(Guid itemId, DataDictionaryItemCreateUpdateDto input);
    Task DeleteItemAsync(Guid itemId);

    /// <summary>按字典编码获取启用的字典项（按排序号升序），供全平台业务下拉/翻译复用。</summary>
    Task<ListResultDto<DataDictionaryItemDto>> GetItemsByCodeAsync(string code);
}
