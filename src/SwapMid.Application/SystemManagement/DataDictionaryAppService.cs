using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.SystemManagement;

[Authorize(SwapMidPermissions.SystemManagement.DataDictionaries.Default)]
public class DataDictionaryAppService : ApplicationService, IDataDictionaryAppService
{
    private readonly IRepository<DataDictionary, Guid> _dictRepository;
    private readonly IRepository<DataDictionaryItem, Guid> _itemRepository;
    private readonly DataDictionaryLookupService _lookupService;

    public DataDictionaryAppService(
        IRepository<DataDictionary, Guid> dictRepository,
        IRepository<DataDictionaryItem, Guid> itemRepository,
        DataDictionaryLookupService lookupService)
    {
        _dictRepository = dictRepository;
        _itemRepository = itemRepository;
        _lookupService = lookupService;
    }

    // ===================== 字典类型 =====================

    public async Task<PagedResultDto<DataDictionaryDto>> GetListAsync(GetDataDictionaryListInput input)
    {
        var query = await _dictRepository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter;
            query = query.Where(x => x.Code.Contains(f) || x.Name.Contains(f));
        }

        if (input.IsEnabled.HasValue)
        {
            query = query.Where(x => x.IsEnabled == input.IsEnabled.Value);
        }

        var total = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query
            .ApplyWhitelistedSorting(input.Sorting, SortWhitelist, "code", logger: Logger)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        // 统计本页字典的字典项数量（一次性查询，避免 N+1）。
        // ToDictionary 无异步等价物：先 ToListAsync 物化（分组计数仍在 SQL 侧）再内存收集。
        var ids = items.Select(x => x.Id).ToList();
        var itemQuery = await _itemRepository.GetQueryableAsync();
        var counts = (await AsyncExecuter.ToListAsync(itemQuery
                .Where(i => ids.Contains(i.DictionaryId))
                .GroupBy(i => i.DictionaryId)
                .Select(g => new { g.Key, Count = g.Count() })))
            .ToDictionary(x => x.Key, x => x.Count);

        var dtos = items.Select(d =>
        {
            var dto = ToDto(d);
            dto.ItemCount = counts.TryGetValue(d.Id, out var c) ? c : 0;
            return dto;
        }).ToList();

        return new PagedResultDto<DataDictionaryDto>(total, dtos);
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）。此前硬编码 OrderBy(Code) 完全忽略 input.Sorting。
    // 默认序保持字典编码升序（结构序）不变。itemCount 是聚合派生列，前端已禁排序。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<DataDictionary>, bool, IOrderedQueryable<DataDictionary>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<DataDictionary>, bool, IOrderedQueryable<DataDictionary>>>
        {
            ["code"] = (q, d) => d ? q.OrderByDescending(x => x.Code) : q.OrderBy(x => x.Code),
            ["name"] = (q, d) => d ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name),
            ["description"] = (q, d) => d ? q.OrderByDescending(x => x.Description) : q.OrderBy(x => x.Description),
            ["isenabled"] = (q, d) => d ? q.OrderByDescending(x => x.IsEnabled) : q.OrderBy(x => x.IsEnabled),
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
            ["lastmodificationtime"] = (q, d) => d ? q.OrderByDescending(x => x.LastModificationTime) : q.OrderBy(x => x.LastModificationTime),
        };

    public async Task<DataDictionaryDto> GetAsync(Guid id)
    {
        var dict = await _dictRepository.GetAsync(id);
        var dto = ToDto(dict);
        dto.ItemCount = await _itemRepository.CountAsync(i => i.DictionaryId == id);
        return dto;
    }

    [Authorize(SwapMidPermissions.SystemManagement.DataDictionaries.Create)]
    public async Task<DataDictionaryDto> CreateAsync(DataDictionaryCreateUpdateDto input)
    {
        await EnsureCodeUniqueAsync(input.Code, null);

        var dict = new DataDictionary(
            GuidGenerator.Create(),
            input.Code,
            input.Name,
            input.Description);

        if (!input.IsEnabled) dict.Disable();

        await _dictRepository.InsertAsync(dict, autoSave: true);
        return ToDto(dict);
    }

    [Authorize(SwapMidPermissions.SystemManagement.DataDictionaries.Update)]
    public async Task<DataDictionaryDto> UpdateAsync(Guid id, DataDictionaryCreateUpdateDto input)
    {
        var dict = await _dictRepository.GetAsync(id);

        await EnsureCodeUniqueAsync(input.Code, id);

        dict.Update(input.Code, input.Name, input.Description);
        if (input.IsEnabled) dict.Enable(); else dict.Disable();

        await _dictRepository.UpdateAsync(dict, autoSave: true);
        var dto = ToDto(dict);
        dto.ItemCount = await _itemRepository.CountAsync(i => i.DictionaryId == id);
        return dto;
    }

    [Authorize(SwapMidPermissions.SystemManagement.DataDictionaries.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        // 级联删除字典项，避免遗留孤儿项。
        await _itemRepository.DeleteAsync(i => i.DictionaryId == id, autoSave: false);
        await _dictRepository.DeleteAsync(id, autoSave: true);
    }

    // ===================== 字典项 =====================

    public async Task<ListResultDto<DataDictionaryItemDto>> GetItemsAsync(Guid dictionaryId)
    {
        var query = await _itemRepository.GetQueryableAsync();
        var items = await AsyncExecuter.ToListAsync(query
            .Where(i => i.DictionaryId == dictionaryId)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Value));
        return new ListResultDto<DataDictionaryItemDto>(items.Select(ToItemDto).ToList());
    }

    [Authorize(SwapMidPermissions.SystemManagement.DataDictionaries.Update)]
    public async Task<DataDictionaryItemDto> CreateItemAsync(DataDictionaryItemCreateUpdateDto input)
    {
        // 校验所属字典存在。
        await _dictRepository.GetAsync(input.DictionaryId);
        await EnsureItemValueUniqueAsync(input.DictionaryId, input.Value, null);

        var item = new DataDictionaryItem(
            GuidGenerator.Create(),
            input.DictionaryId,
            input.Label,
            input.Value,
            input.SortOrder,
            input.Description);

        if (!input.IsEnabled) item.Disable();

        await _itemRepository.InsertAsync(item, autoSave: true);
        return ToItemDto(item);
    }

    [Authorize(SwapMidPermissions.SystemManagement.DataDictionaries.Update)]
    public async Task<DataDictionaryItemDto> UpdateItemAsync(Guid itemId, DataDictionaryItemCreateUpdateDto input)
    {
        var item = await _itemRepository.GetAsync(itemId);
        await EnsureItemValueUniqueAsync(item.DictionaryId, input.Value, itemId);

        item.Update(input.Label, input.Value, input.SortOrder, input.Description);
        if (input.IsEnabled) item.Enable(); else item.Disable();

        await _itemRepository.UpdateAsync(item, autoSave: true);
        return ToItemDto(item);
    }

    [Authorize(SwapMidPermissions.SystemManagement.DataDictionaries.Update)]
    public async Task DeleteItemAsync(Guid itemId)
    {
        await _itemRepository.DeleteAsync(itemId, autoSave: true);
    }

    // ===================== 消费接口 =====================

    /// <summary>
    /// 按字典编码取启用字典项。查询逻辑已下沉至 <see cref="DataDictionaryLookupService"/>（后台线程如 Hangfire
    /// 定时任务无用户身份，接口注入的带权 AppService 会因本方法的 <c>[Authorize]</c> 门面必炸
    /// <see cref="Volo.Abp.Authorization.AbpAuthorizationException"/>，见蓝图 §17 缺陷记录）；
    /// 本方法只保留 <c>[Authorize]</c> 门面一行委托，鉴权面与行为均不变。
    /// </summary>
    [Authorize]
    public Task<ListResultDto<DataDictionaryItemDto>> GetItemsByCodeAsync(string code)
        => _lookupService.GetItemsByCodeAsync(code);

    // ===================== 私有辅助 =====================

    private async Task EnsureCodeUniqueAsync(string code, Guid? excludeId)
    {
        var query = await _dictRepository.GetQueryableAsync();
        var exists = await AsyncExecuter.AnyAsync(
            query.Where(d => d.Code == code && (excludeId == null || d.Id != excludeId.Value)));
        if (exists)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.SystemManagement.DataDictionaryCodeExists)
                .WithData("code", code);
        }
    }

    private async Task EnsureItemValueUniqueAsync(Guid dictionaryId, string value, Guid? excludeId)
    {
        var query = await _itemRepository.GetQueryableAsync();
        var exists = await AsyncExecuter.AnyAsync(query.Where(i =>
            i.DictionaryId == dictionaryId &&
            i.Value == value &&
            (excludeId == null || i.Id != excludeId.Value)));
        if (exists)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.SystemManagement.DataDictionaryItemValueExists)
                .WithData("value", value);
        }
    }

    private static DataDictionaryDto ToDto(DataDictionary d) => new()
    {
        Id = d.Id,
        Code = d.Code,
        Name = d.Name,
        Description = d.Description,
        IsEnabled = d.IsEnabled,
        CreationTime = d.CreationTime,
        CreatorId = d.CreatorId,
        LastModificationTime = d.LastModificationTime,
        LastModifierId = d.LastModifierId,
    };

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
