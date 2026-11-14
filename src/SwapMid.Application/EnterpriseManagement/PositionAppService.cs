using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Permissions;
using SwapMid.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.EnterpriseManagement;

[Authorize(SwapMidPermissions.EnterpriseManagement.Positions.Default)]
public class PositionAppService :
    CrudAppService<Position, PositionDto, Guid, GetPositionListInput, PositionCreateUpdateDto>,
    IPositionAppService
{
    private readonly IRepository<Enterprise, Guid> _enterpriseRepository;
    private readonly IRepository<CompanyDepartment, Guid> _departmentRepository;

    public PositionAppService(
        IRepository<Position, Guid> repository,
        IRepository<Enterprise, Guid> enterpriseRepository,
        IRepository<CompanyDepartment, Guid> departmentRepository)
        : base(repository)
    {
        _enterpriseRepository = enterpriseRepository;
        _departmentRepository = departmentRepository;
        GetPolicyName = SwapMidPermissions.EnterpriseManagement.Positions.Default;
        GetListPolicyName = SwapMidPermissions.EnterpriseManagement.Positions.Default;
        CreatePolicyName = SwapMidPermissions.EnterpriseManagement.Positions.Create;
        UpdatePolicyName = SwapMidPermissions.EnterpriseManagement.Positions.Update;
        DeletePolicyName = SwapMidPermissions.EnterpriseManagement.Positions.Delete;
    }

    public override async Task<PagedResultDto<PositionDto>> GetListAsync(GetPositionListInput input)
    {
        var query = await Repository.GetQueryableAsync();
        var enterprise = await FindEnterpriseAsync(input.EnterpriseCode, input.EnterpriseId);

        if (HasEnterpriseFilter(input) && enterprise == null)
        {
            return new PagedResultDto<PositionDto>(0, new List<PositionDto>());
        }

        if (enterprise != null)
            query = query.Where(x => x.EnterpriseId == enterprise.Id);

        if (input.DepartmentId.HasValue)
            query = query.Where(x => x.DepartmentId == input.DepartmentId.Value);
        else if (!string.IsNullOrWhiteSpace(input.DepartmentCode))
        {
            var departmentQuery = (await _departmentRepository.GetQueryableAsync())
                .Where(x => x.Code == input.DepartmentCode);

            if (enterprise != null)
            {
                departmentQuery = departmentQuery.Where(x => x.EnterpriseId == enterprise.Id);
            }

            var departmentIds = await AsyncExecuter.ToArrayAsync(departmentQuery.Select(x => x.Id));
            query = query.Where(x => departmentIds.Contains(x.DepartmentId));
        }

        if (input.IsEnabled.HasValue)
            query = query.Where(x => x.IsEnabled == input.IsEnabled.Value);

        if (!string.IsNullOrWhiteSpace(input.Filter))
            query = query.Where(x => x.Code.Contains(input.Filter) || x.Name.Contains(input.Filter));

        query = query.ApplyTimeRange(input.StartTime, input.EndTime, input.TimeType);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query
            .OrderBy(x => x.Order).ThenBy(x => x.Code)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        var dtos = items.Select(x => x.ToDto()).ToList();
        await FillNamesAsync(dtos, items);

        return new PagedResultDto<PositionDto>(totalCount, dtos);
    }

    public override async Task<PositionDto> GetAsync(Guid id)
    {
        var entity = await Repository.GetAsync(id);
        var dto = entity.ToDto();
        await FillNamesAsync(new List<PositionDto> { dto }, new List<Position> { entity });
        return dto;
    }

    [Authorize(SwapMidPermissions.EnterpriseManagement.Positions.Create)]
    public override async Task<PositionDto> CreateAsync(PositionCreateUpdateDto input)
    {
        var references = await ResolveReferencesAsync(input);

        var position = new Position(
            GuidGenerator.Create(),
            references.EnterpriseId,
            references.DepartmentId,
            input.Code,
            input.Name,
            input.Level,
            input.Description,
            input.Order);

        if (!input.IsEnabled) position.Disable();

        await Repository.InsertAsync(position, autoSave: true);
        var dto = position.ToDto();
        await FillNamesAsync(new List<PositionDto> { dto }, new List<Position> { position });
        return dto;
    }

    [Authorize(SwapMidPermissions.EnterpriseManagement.Positions.Update)]
    public override async Task<PositionDto> UpdateAsync(Guid id, PositionCreateUpdateDto input)
    {
        var position = await Repository.GetAsync(id);
        var references = await ResolveReferencesAsync(input);

        if (references.EnterpriseId != position.EnterpriseId)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.EnterpriseNotFound)
                .WithData("enterpriseId", references.EnterpriseId);
        }

        position.Update(references.DepartmentId, input.Code, input.Name, input.Level, input.Description, input.Order);
        if (input.IsEnabled) position.Enable(); else position.Disable();

        await Repository.UpdateAsync(position, autoSave: true);
        var dto = position.ToDto();
        await FillNamesAsync(new List<PositionDto> { dto }, new List<Position> { position });
        return dto;
    }

    [Authorize(SwapMidPermissions.EnterpriseManagement.Positions.Delete)]
    public Task<BatchOperationResultDto> BatchDeleteAsync(BatchIdsInput input)
        => BatchOperationExecutor.DeleteAsync(Repository, input.Ids);

    [Authorize(SwapMidPermissions.EnterpriseManagement.Positions.Update)]
    public Task<BatchOperationResultDto> BatchSetEnabledAsync(BatchSetEnabledInput input)
        => BatchOperationExecutor.UpdateAsync(
            Repository,
            input.Ids,
            position =>
            {
                if (input.IsEnabled) position.Enable(); else position.Disable();
            });

    private async Task<PositionReferences> ResolveReferencesAsync(PositionCreateUpdateDto input)
    {
        var enterprise = await FindEnterpriseAsync(input.EnterpriseCode, input.EnterpriseId);
        if (enterprise == null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.EnterpriseNotFound)
                .WithData("enterpriseCode", input.EnterpriseCode ?? string.Empty)
                .WithData("enterpriseId", input.EnterpriseId?.ToString() ?? string.Empty);
        }

        var departmentId = input.DepartmentId;
        if (!string.IsNullOrWhiteSpace(input.DepartmentCode))
        {
            var departmentByCode = await FindDepartmentByCodeAsync(enterprise.Id, input.DepartmentCode);
            if (departmentByCode == null)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.DepartmentNotFound)
                    .WithData("departmentCode", input.DepartmentCode);
            }

            departmentId = departmentByCode.Id;
        }

        if (!departmentId.HasValue)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.DepartmentNotFound)
                .WithData("departmentCode", input.DepartmentCode ?? string.Empty)
                .WithData("departmentId", input.DepartmentId?.ToString() ?? string.Empty);
        }

        var dept = await _departmentRepository.FindAsync(departmentId.Value);
        if (dept == null || dept.EnterpriseId != enterprise.Id)
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.DepartmentNotFound)
                .WithData("departmentId", departmentId.Value);

        return new PositionReferences(enterprise.Id, departmentId.Value);
    }

    private async Task FillNamesAsync(List<PositionDto> dtos, List<Position> entities)
    {
        var enterpriseIds = entities.Select(x => x.EnterpriseId).Distinct().ToArray();
        var departmentIds = entities.Select(x => x.DepartmentId).Distinct().ToArray();

        var enterprises = (await AsyncExecuter.ToListAsync((await _enterpriseRepository.GetQueryableAsync())
                .Where(x => enterpriseIds.Contains(x.Id))))
            .ToDictionary(x => x.Id);
        var departments = (await AsyncExecuter.ToListAsync((await _departmentRepository.GetQueryableAsync())
                .Where(x => departmentIds.Contains(x.Id))))
            .ToDictionary(x => x.Id);

        foreach (var dto in dtos)
        {
            var enterprise = enterprises.GetValueOrDefault(dto.EnterpriseId);
            var department = departments.GetValueOrDefault(dto.DepartmentId);

            dto.EnterpriseCode = enterprise?.Code;
            dto.EnterpriseName = enterprise?.Name;
            dto.Enterprise = enterprise == null ? null : ToReference(enterprise);
            dto.DepartmentCode = department?.Code;
            dto.DepartmentName = department?.Name;
            dto.Department = department == null ? null : ToReference(department);
        }
    }

    private static bool HasEnterpriseFilter(GetPositionListInput input)
    {
        return input.EnterpriseId.HasValue || !string.IsNullOrWhiteSpace(input.EnterpriseCode);
    }

    private async Task<Enterprise?> FindEnterpriseAsync(string? enterpriseCode, Guid? enterpriseId)
    {
        if (!string.IsNullOrWhiteSpace(enterpriseCode))
        {
            return await AsyncExecuter.FirstOrDefaultAsync(
                (await _enterpriseRepository.GetQueryableAsync()).Where(x => x.Code == enterpriseCode));
        }

        return enterpriseId.HasValue ? await _enterpriseRepository.FindAsync(enterpriseId.Value) : null;
    }

    private async Task<CompanyDepartment?> FindDepartmentByCodeAsync(Guid enterpriseId, string code)
    {
        return await AsyncExecuter.FirstOrDefaultAsync((await _departmentRepository.GetQueryableAsync())
            .Where(x => x.EnterpriseId == enterpriseId && x.Code == code));
    }

    private static EntityReferenceDto ToReference(Enterprise enterprise) => new()
    {
        Id = enterprise.Id,
        Code = enterprise.Code,
        Name = enterprise.Name
    };

    private static EntityReferenceDto ToReference(CompanyDepartment department) => new()
    {
        Id = department.Id,
        Code = department.Code,
        Name = department.Name
    };

    private sealed record PositionReferences(Guid EnterpriseId, Guid DepartmentId);
}
