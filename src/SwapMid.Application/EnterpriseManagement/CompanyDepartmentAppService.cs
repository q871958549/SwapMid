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

[Authorize(SwapMidPermissions.EnterpriseManagement.Departments.Default)]
public class CompanyDepartmentAppService :
    CrudAppService<CompanyDepartment, CompanyDepartmentDto, Guid, GetCompanyDepartmentListInput, CompanyDepartmentCreateUpdateDto>,
    ICompanyDepartmentAppService
{
    private readonly ICompanyDepartmentRepository _departmentRepository;
    private readonly IRepository<Enterprise, Guid> _enterpriseRepository;
    private readonly IRepository<Employee, Guid> _employeeRepository;

    public CompanyDepartmentAppService(
        IRepository<CompanyDepartment, Guid> repository,
        ICompanyDepartmentRepository departmentRepository,
        IRepository<Enterprise, Guid> enterpriseRepository,
        IRepository<Employee, Guid> employeeRepository)
        : base(repository)
    {
        _departmentRepository = departmentRepository;
        _enterpriseRepository = enterpriseRepository;
        _employeeRepository = employeeRepository;
        GetPolicyName = SwapMidPermissions.EnterpriseManagement.Departments.Default;
        GetListPolicyName = SwapMidPermissions.EnterpriseManagement.Departments.Default;
        CreatePolicyName = SwapMidPermissions.EnterpriseManagement.Departments.Create;
        UpdatePolicyName = SwapMidPermissions.EnterpriseManagement.Departments.Update;
        DeletePolicyName = SwapMidPermissions.EnterpriseManagement.Departments.Delete;
    }

    public override async Task<PagedResultDto<CompanyDepartmentDto>> GetListAsync(GetCompanyDepartmentListInput input)
    {
        var query = await Repository.GetQueryableAsync();
        var enterprise = await FindEnterpriseAsync(input.EnterpriseCode, input.EnterpriseId);

        if (HasEnterpriseFilter(input) && enterprise == null)
        {
            return new PagedResultDto<CompanyDepartmentDto>(0, new List<CompanyDepartmentDto>());
        }

        if (enterprise != null)
        {
            query = query.Where(x => x.EnterpriseId == enterprise.Id);
        }

        if (input.ParentId.HasValue)
        {
            query = query.Where(x => x.ParentId == input.ParentId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(input.ParentCode))
        {
            var parentQuery = (await Repository.GetQueryableAsync())
                .Where(x => x.Code == input.ParentCode);

            if (enterprise != null)
            {
                parentQuery = parentQuery.Where(x => x.EnterpriseId == enterprise.Id);
            }

            var parentIds = await AsyncExecuter.ToArrayAsync(parentQuery.Select(x => x.Id));
            query = query.Where(x => x.ParentId.HasValue && parentIds.Contains(x.ParentId.Value));
        }

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            query = query.Where(x => x.Code.Contains(input.Filter) || x.Name.Contains(input.Filter));
        }

        query = query.ApplyTimeRange(input.StartTime, input.EndTime, input.TimeType);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Code)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        var dtos = items.Select(x => x.ToDto()).ToList();
        await FillNamesAsync(dtos, items);
        return new PagedResultDto<CompanyDepartmentDto>(totalCount, dtos);
    }

    public override async Task<CompanyDepartmentDto> GetAsync(Guid id)
    {
        var department = await Repository.GetAsync(id);
        var dto = department.ToDto();
        await FillNamesAsync(new List<CompanyDepartmentDto> { dto }, new List<CompanyDepartment> { department });
        return dto;
    }

    [Authorize(SwapMidPermissions.EnterpriseManagement.Departments.Create)]
    public override async Task<CompanyDepartmentDto> CreateAsync(CompanyDepartmentCreateUpdateDto input)
    {
        var references = await ResolveReferencesAsync(input);

        if (await _departmentRepository.CodeExistsAsync(references.EnterpriseId, input.Code))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.DepartmentCodeAlreadyExists)
                .WithData("code", input.Code);
        }

        var department = new CompanyDepartment(
            GuidGenerator.Create(),
            references.EnterpriseId,
            input.Code,
            input.Name,
            references.ParentId,
            references.LeaderEmployeeId,
            input.Order);
        await ApplyHierarchyAsync(department, input, references.ParentId);
        if (input.IsEnabled) department.Enable(); else department.Disable();

        await Repository.InsertAsync(department, autoSave: true);
        var dto = department.ToDto();
        await FillNamesAsync(new List<CompanyDepartmentDto> { dto }, new List<CompanyDepartment> { department });
        return dto;
    }

    [Authorize(SwapMidPermissions.EnterpriseManagement.Departments.Update)]
    public override async Task<CompanyDepartmentDto> UpdateAsync(Guid id, CompanyDepartmentCreateUpdateDto input)
    {
        var department = await Repository.GetAsync(id);
        var references = await ResolveReferencesAsync(input);

        if (references.EnterpriseId != department.EnterpriseId)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.EnterpriseNotFound)
                .WithData("enterpriseId", references.EnterpriseId);
        }

        if (references.ParentId == id)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.DepartmentNotFound)
                .WithData("parentId", references.ParentId);
        }

        if (await _departmentRepository.CodeExistsAsync(references.EnterpriseId, input.Code, excludeId: id))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.DepartmentCodeAlreadyExists)
                .WithData("code", input.Code);
        }

        department.Update(input.Code, input.Name, references.ParentId, references.LeaderEmployeeId, input.Order);
        await ApplyHierarchyAsync(department, input, references.ParentId);
        if (input.IsEnabled) department.Enable(); else department.Disable();

        await Repository.UpdateAsync(department, autoSave: true);
        var dto = department.ToDto();
        await FillNamesAsync(new List<CompanyDepartmentDto> { dto }, new List<CompanyDepartment> { department });
        return dto;
    }

    [Authorize(SwapMidPermissions.EnterpriseManagement.Departments.Delete)]
    public Task<BatchOperationResultDto> BatchDeleteAsync(BatchIdsInput input)
        => BatchOperationExecutor.DeleteAsync(Repository, input.Ids);

    [Authorize(SwapMidPermissions.EnterpriseManagement.Departments.Update)]
    public Task<BatchOperationResultDto> BatchSetEnabledAsync(BatchSetEnabledInput input)
        => BatchOperationExecutor.UpdateAsync(
            Repository,
            input.Ids,
            department =>
            {
                if (input.IsEnabled) department.Enable(); else department.Disable();
            });

    private async Task<DepartmentReferences> ResolveReferencesAsync(CompanyDepartmentCreateUpdateDto input)
    {
        var enterprise = await FindEnterpriseAsync(input.EnterpriseCode, input.EnterpriseId);
        if (enterprise == null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.EnterpriseNotFound)
                .WithData("enterpriseCode", input.EnterpriseCode ?? string.Empty)
                .WithData("enterpriseId", input.EnterpriseId?.ToString() ?? string.Empty);
        }

        var parentId = input.ParentId;
        if (!string.IsNullOrWhiteSpace(input.ParentCode))
        {
            var parent = await FindDepartmentByCodeAsync(enterprise.Id, input.ParentCode);
            if (parent == null)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.DepartmentNotFound)
                    .WithData("parentCode", input.ParentCode);
            }

            parentId = parent.Id;
        }

        if (parentId.HasValue)
        {
            var parent = await Repository.FindAsync(parentId.Value);
            if (parent == null || parent.EnterpriseId != enterprise.Id)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.DepartmentNotFound)
                    .WithData("parentId", parentId.Value);
            }
        }

        var leaderEmployeeId = input.LeaderEmployeeId;
        if (!string.IsNullOrWhiteSpace(input.LeaderEmployeeNo))
        {
            var leader = await FindEmployeeByNoAsync(enterprise.Id, input.LeaderEmployeeNo);
            if (leader == null)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.EmployeeNotFound)
                    .WithData("leaderEmployeeNo", input.LeaderEmployeeNo);
            }

            leaderEmployeeId = leader.Id;
        }

        if (leaderEmployeeId.HasValue)
        {
            var leader = await _employeeRepository.FindAsync(leaderEmployeeId.Value);
            if (leader == null || leader.EnterpriseId != enterprise.Id)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.EmployeeNotFound)
                    .WithData("leaderEmployeeId", leaderEmployeeId.Value);
            }
        }

        return new DepartmentReferences(enterprise.Id, parentId, leaderEmployeeId);
    }

    private async Task FillNamesAsync(List<CompanyDepartmentDto> dtos, List<CompanyDepartment> entities)
    {
        var enterpriseIds = entities.Select(x => x.EnterpriseId).Distinct().ToArray();
        var parentIds = entities.Where(x => x.ParentId.HasValue).Select(x => x.ParentId!.Value).Distinct().ToArray();
        var leaderIds = entities.Where(x => x.LeaderEmployeeId.HasValue).Select(x => x.LeaderEmployeeId!.Value).Distinct().ToArray();

        var enterprises = (await AsyncExecuter.ToListAsync((await _enterpriseRepository.GetQueryableAsync())
                .Where(x => enterpriseIds.Contains(x.Id))))
            .ToDictionary(x => x.Id);
        var parents = (await AsyncExecuter.ToListAsync((await Repository.GetQueryableAsync())
                .Where(x => parentIds.Contains(x.Id))))
            .ToDictionary(x => x.Id);
        var leaders = (await AsyncExecuter.ToListAsync((await _employeeRepository.GetQueryableAsync())
                .Where(x => leaderIds.Contains(x.Id))))
            .ToDictionary(x => x.Id);

        foreach (var dto in dtos)
        {
            var enterprise = enterprises.GetValueOrDefault(dto.EnterpriseId);
            var parent = dto.ParentId.HasValue ? parents.GetValueOrDefault(dto.ParentId.Value) : null;
            var leader = dto.LeaderEmployeeId.HasValue ? leaders.GetValueOrDefault(dto.LeaderEmployeeId.Value) : null;

            dto.EnterpriseCode = enterprise?.Code;
            dto.EnterpriseName = enterprise?.Name;
            dto.Enterprise = enterprise == null ? null : ToReference(enterprise);
            dto.ParentCode = parent?.Code;
            dto.ParentName = parent?.Name;
            dto.Parent = parent == null ? null : ToReference(parent);
            dto.LeaderEmployeeNo = leader?.EmployeeNo;
            dto.LeaderEmployeeName = leader?.Name;
            dto.LeaderEmployee = leader == null ? null : ToReference(leader);
        }
    }

    private async Task ApplyHierarchyAsync(
        CompanyDepartment department,
        CompanyDepartmentCreateUpdateDto input,
        Guid? parentId)
    {
        CompanyDepartment? parent = null;
        if (parentId.HasValue)
        {
            parent = await Repository.FindAsync(parentId.Value);
        }

        var computedLevel = (parent?.Level ?? 0) + 1;
        var level = input.Level > 1 ? input.Level : computedLevel;
        var parentPath = parent?.FullPath ?? parent?.Name;
        var fullPath = !string.IsNullOrWhiteSpace(input.FullPath)
            ? input.FullPath
            : string.IsNullOrWhiteSpace(parentPath)
                ? input.Name
                : $"{parentPath}/{input.Name}";

        department.SetHierarchy(level, fullPath);
    }

    private static bool HasEnterpriseFilter(GetCompanyDepartmentListInput input)
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
        return await AsyncExecuter.FirstOrDefaultAsync((await Repository.GetQueryableAsync())
            .Where(x => x.EnterpriseId == enterpriseId && x.Code == code));
    }

    private async Task<Employee?> FindEmployeeByNoAsync(Guid enterpriseId, string employeeNo)
    {
        return await AsyncExecuter.FirstOrDefaultAsync((await _employeeRepository.GetQueryableAsync())
            .Where(x => x.EnterpriseId == enterpriseId && x.EmployeeNo == employeeNo));
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

    private static EntityReferenceDto ToReference(Employee employee) => new()
    {
        Id = employee.Id,
        Code = employee.EmployeeNo,
        Name = employee.Name
    };

    private sealed record DepartmentReferences(Guid EnterpriseId, Guid? ParentId, Guid? LeaderEmployeeId);
}
