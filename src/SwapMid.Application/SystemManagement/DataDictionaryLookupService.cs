using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace SwapMid.SystemManagement;

/// <summary>
/// 数据字典查询内核（§17 缺陷修复，蓝图 docs/project-management-blueprint.md）：承载
/// <see cref="DataDictionaryAppService.GetItemsByCodeAsync"/> 的查询逻辑，供无用户身份场景下的内部调用方使用。
///
/// 为什么存在：Hangfire 后台线程无 <c>CurrentUser</c>（匿名），而 <see cref="IDataDictionaryAppService"/>
/// 是接口注入——ABP 接口代理拦截该接口的一切方法（不止 virtual 方法），凡带 <c>[Authorize]</c> 的调用在无身份线程
/// 必炸 <see cref="Volo.Abp.Authorization.AbpAuthorizationException"/>。本类是 <c>ITransientDependency</c> 普通类
/// （非 AppService、无鉴权特性），后台任务（如样品数据自动同步）改注入本类即可绕开该问题，且不提权、不新增匿名 HTTP 端点。
/// HTTP 门面 <see cref="DataDictionaryAppService.GetItemsByCodeAsync"/> 保留 <c>[Authorize]</c> 一行委托本类，
/// 鉴权面与契约均零变化。
/// </summary>
public class DataDictionaryLookupService : ITransientDependency
{
    private readonly IRepository<DataDictionary, Guid> _dictRepository;
    private readonly IRepository<DataDictionaryItem, Guid> _itemRepository;
    // 本类刻意不是 AppService（见上方类注释），故无 AsyncExecuter 基类属性；
    // 而 Application 层禁 using EF Core，IAsyncQueryableExecuter 是 ABP 为此准备的正门。
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public DataDictionaryLookupService(
        IRepository<DataDictionary, Guid> dictRepository,
        IRepository<DataDictionaryItem, Guid> itemRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _dictRepository = dictRepository;
        _itemRepository = itemRepository;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>按字典编码取启用字典项（按 <c>SortOrder</c>/<c>Value</c> 排序）；编码不存在或未启用返回空列表。</summary>
    public async Task<ListResultDto<DataDictionaryItemDto>> GetItemsByCodeAsync(string code)
    {
        var dictQuery = await _dictRepository.GetQueryableAsync();
        var dict = await _asyncExecuter.FirstOrDefaultAsync(
            dictQuery.Where(d => d.Code == code && d.IsEnabled));
        if (dict == null)
        {
            return new ListResultDto<DataDictionaryItemDto>(new List<DataDictionaryItemDto>());
        }

        var itemQuery = await _itemRepository.GetQueryableAsync();
        var items = await _asyncExecuter.ToListAsync(itemQuery
            .Where(i => i.DictionaryId == dict.Id && i.IsEnabled)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Value));
        return new ListResultDto<DataDictionaryItemDto>(items.Select(ToItemDto).ToList());
    }

    private static DataDictionaryItemDto ToItemDto(DataDictionaryItem i) => new()
    {
        Id = i.Id,
        DictionaryId = i.DictionaryId,
        Label = i.Label,
        Value = i.Value,
        SortOrder = i.SortOrder,
        IsEnabled = i.IsEnabled,
        Description = i.Description,
        CreationTime = i.CreationTime,
        CreatorId = i.CreatorId,
        LastModificationTime = i.LastModificationTime,
        LastModifierId = i.LastModifierId,
    };
}
