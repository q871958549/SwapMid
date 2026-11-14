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

[Authorize(SwapMidPermissions.ApiManagement.ApiRoutes.Default)]
public class ApiRouteAppService :
    CrudAppService<ApiRoute, ApiRouteDto, Guid, GetApiRouteListInput, ApiRouteCreateUpdateDto>,
    IApiRouteAppService
{
    private readonly IApiRouteRepository _apiRouteRepository;
    private readonly IRepository<ApiGroup, Guid> _groupRepository;

    public ApiRouteAppService(
        IRepository<ApiRoute, Guid> repository,
        IApiRouteRepository apiRouteRepository,
        IRepository<ApiGroup, Guid> groupRepository)
        : base(repository)
    {
        _apiRouteRepository = apiRouteRepository;
        _groupRepository = groupRepository;
        GetPolicyName = SwapMidPermissions.ApiManagement.ApiRoutes.Default;
        GetListPolicyName = SwapMidPermissions.ApiManagement.ApiRoutes.Default;
        CreatePolicyName = SwapMidPermissions.ApiManagement.ApiRoutes.Create;
        UpdatePolicyName = SwapMidPermissions.ApiManagement.ApiRoutes.Update;
        DeletePolicyName = SwapMidPermissions.ApiManagement.ApiRoutes.Delete;
    }

    public override async Task<PagedResultDto<ApiRouteDto>> GetListAsync(GetApiRouteListInput input)
    {
        var totalCount = await _apiRouteRepository.GetCountAsync(
            input.Filter, input.GroupId, input.IsEnabled,
            input.StartTime, input.EndTime, input.TimeType);
        var items = await _apiRouteRepository.GetListAsync(
            input.Filter, input.GroupId, input.IsEnabled,
            input.StartTime, input.EndTime, input.TimeType,
            input.Sorting, input.MaxResultCount, input.SkipCount);

        var groupIds = items.Where(x => x.GroupId.HasValue).Select(x => x.GroupId!.Value).Distinct().ToList();
        var groups = groupIds.Any()
            ? (await _groupRepository.GetListAsync(x => groupIds.Contains(x.Id)))
              .ToDictionary(x => x.Id, x => x.Name)
            : new Dictionary<Guid, string>();

        var dtos = items.Select(route =>
        {
            var dto = route.ToDto();
            if (route.GroupId.HasValue && groups.TryGetValue(route.GroupId.Value, out var groupName))
                dto.GroupName = groupName;
            return dto;
        }).ToList();

        return new PagedResultDto<ApiRouteDto>(totalCount, dtos);
    }

    public override async Task<ApiRouteDto> GetAsync(Guid id)
    {
        var route = await Repository.GetAsync(id);
        var dto = route.ToDto();
        if (route.GroupId.HasValue)
        {
            var group = await _groupRepository.FindAsync(route.GroupId.Value);
            dto.GroupName = group?.Name;
        }
        return dto;
    }

    public override async Task<ApiRouteDto> CreateAsync(ApiRouteCreateUpdateDto input)
    {
        if (await _apiRouteRepository.PathAndMethodExistsAsync(input.Path, input.HttpMethod))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.ApiManagement.RoutePathAlreadyExists)
                .WithData("path", input.Path).WithData("method", input.HttpMethod.ToString());
        }

        var route = new ApiRoute(
            GuidGenerator.Create(), input.Name, input.Path, input.HttpMethod,
            input.UpstreamUrl, input.GroupId, input.Description, input.Order);
        if (!input.IsEnabled) route.Disable();

        await Repository.InsertAsync(route, autoSave: true);
        return route.ToDto();
    }

    public override async Task<ApiRouteDto> UpdateAsync(Guid id, ApiRouteCreateUpdateDto input)
    {
        var route = await Repository.GetAsync(id);

        if (await _apiRouteRepository.PathAndMethodExistsAsync(input.Path, input.HttpMethod, excludeId: id))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.ApiManagement.RoutePathAlreadyExists)
                .WithData("path", input.Path).WithData("method", input.HttpMethod.ToString());
        }

        route.Update(input.Name, input.Path, input.HttpMethod,
            input.UpstreamUrl, input.GroupId, input.Description, input.Order);
        if (input.IsEnabled) route.Enable(); else route.Disable();

        await Repository.UpdateAsync(route, autoSave: true);
        return route.ToDto();
    }

    [Authorize(SwapMidPermissions.ApiManagement.ApiRoutes.Delete)]
    public override Task DeleteAsync(Guid id) => base.DeleteAsync(id);

    [Authorize(SwapMidPermissions.ApiManagement.ApiRoutes.Update)]
    public async Task EnableAsync(Guid id)
    {
        var route = await Repository.GetAsync(id);
        route.Enable();
        await Repository.UpdateAsync(route, autoSave: true);
    }

    [Authorize(SwapMidPermissions.ApiManagement.ApiRoutes.Update)]
    public async Task DisableAsync(Guid id)
    {
        var route = await Repository.GetAsync(id);
        route.Disable();
        await Repository.UpdateAsync(route, autoSave: true);
    }
}
