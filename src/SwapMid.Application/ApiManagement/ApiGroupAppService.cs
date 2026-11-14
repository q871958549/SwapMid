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

namespace SwapMid.ApiManagement;

[Authorize(SwapMidPermissions.ApiManagement.ApiGroups.Default)]
public class ApiGroupAppService :
    CrudAppService<ApiGroup, ApiGroupDto, Guid, GetApiGroupListInput, ApiGroupCreateUpdateDto>,
    IApiGroupAppService
{
    private readonly IApiGroupRepository _apiGroupRepository;

    public ApiGroupAppService(
        IRepository<ApiGroup, Guid> repository,
        IApiGroupRepository apiGroupRepository)
        : base(repository)
    {
        _apiGroupRepository = apiGroupRepository;
        GetPolicyName = SwapMidPermissions.ApiManagement.ApiGroups.Default;
        GetListPolicyName = SwapMidPermissions.ApiManagement.ApiGroups.Default;
        CreatePolicyName = SwapMidPermissions.ApiManagement.ApiGroups.Create;
        UpdatePolicyName = SwapMidPermissions.ApiManagement.ApiGroups.Update;
        DeletePolicyName = SwapMidPermissions.ApiManagement.ApiGroups.Delete;
    }

    public override async Task<PagedResultDto<ApiGroupDto>> GetListAsync(GetApiGroupListInput input)
    {
        var query = await Repository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            query = query.Where(x =>
                x.Name.Contains(input.Filter) ||
                (x.Description != null && x.Description.Contains(input.Filter)));
        }

        query = query.ApplyTimeRange(input.StartTime, input.EndTime, input.TimeType);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query
            // appendIdTiebreaker: false —— 调用点自带 Name 兜底（见下方注释）。
            .ApplyWhitelistedSorting(input.Sorting, SortWhitelist, "order", appendIdTiebreaker: false)
            .ThenBy(x => x.Name)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<ApiGroupDto>(totalCount, items.Select(x => x.ToDto()).ToList());
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）：未命中回退 Order（默认展示顺序，越小越靠前），
    // 杜绝把用户串裸传动态排序（排序注入）。ApplyWhitelistedSorting 之后仍保留 ThenBy(Name) 兜底：
    // Order 常见同值（未手动调整排序号时均为默认值 0），不加二级排序键会导致同值行在分页 Skip/Take
    // 下顺序不确定，翻页时错位/重复——沿用迁移前的原有兜底语义。
    // routeCount 是前端历史遗留死列（ApiGroupDto 并无此字段，恒为 0），不属于本次任务范围，
    // 不进白名单、也不改前端（前端该列不加 enableSorting: false，避免顺手动一个无关 bug）。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<ApiGroup>, bool, IOrderedQueryable<ApiGroup>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<ApiGroup>, bool, IOrderedQueryable<ApiGroup>>>
        {
            ["order"] = (q, d) => d ? q.OrderByDescending(x => x.Order) : q.OrderBy(x => x.Order),
            ["name"] = (q, d) => d ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name),
            ["description"] = (q, d) => d ? q.OrderByDescending(x => x.Description) : q.OrderBy(x => x.Description),
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
        };

    public override async Task<ApiGroupDto> GetAsync(Guid id)
    {
        var group = await Repository.GetAsync(id);
        return group.ToDto();
    }

    public override async Task<ApiGroupDto> CreateAsync(ApiGroupCreateUpdateDto input)
    {
        if (await _apiGroupRepository.NameExistsAsync(input.Name))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.ApiManagement.GroupNameAlreadyExists)
                .WithData("name", input.Name);
        }

        var group = new ApiGroup(GuidGenerator.Create(), input.Name, input.Description, input.Order);
        if (!input.IsEnabled) group.Disable();

        await Repository.InsertAsync(group, autoSave: true);
        return group.ToDto();
    }

    public override async Task<ApiGroupDto> UpdateAsync(Guid id, ApiGroupCreateUpdateDto input)
    {
        var group = await Repository.GetAsync(id);

        if (await _apiGroupRepository.NameExistsAsync(input.Name, excludeId: id))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.ApiManagement.GroupNameAlreadyExists)
                .WithData("name", input.Name);
        }

        group.Update(input.Name, input.Description, input.Order);
        if (input.IsEnabled) group.Enable(); else group.Disable();

        await Repository.UpdateAsync(group, autoSave: true);
        return group.ToDto();
    }

    [Authorize(SwapMidPermissions.ApiManagement.ApiGroups.Delete)]
    public override Task DeleteAsync(Guid id) => base.DeleteAsync(id);
}
