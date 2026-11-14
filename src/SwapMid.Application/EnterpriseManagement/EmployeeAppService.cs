using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.SharedKernel;
using SwapMid.SharedKernel.Employees;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace SwapMid.EnterpriseManagement;

[Authorize(SwapMidPermissions.EnterpriseManagement.Employees.Default)]
public class EmployeeAppService :
    CrudAppService<Employee, EmployeeDto, Guid, GetEmployeeListInput, EmployeeCreateUpdateDto>,
    IEmployeeAppService
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IRepository<Enterprise, Guid> _enterpriseRepository;
    private readonly ICompanyDepartmentRepository _departmentRepository;
    private readonly IRepository<Position, Guid> _positionRepository;
    private readonly EmployeeBatchOperationService _batchOperationService;

    public EmployeeAppService(
        IRepository<Employee, Guid> repository,
        IEmployeeRepository employeeRepository,
        IRepository<Enterprise, Guid> enterpriseRepository,
        ICompanyDepartmentRepository departmentRepository,
        IRepository<Position, Guid> positionRepository,
        EmployeeBatchOperationService batchOperationService)
        : base(repository)
    {
        _employeeRepository = employeeRepository;
        _enterpriseRepository = enterpriseRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _batchOperationService = batchOperationService;
        GetPolicyName = SwapMidPermissions.EnterpriseManagement.Employees.Default;
        GetListPolicyName = SwapMidPermissions.EnterpriseManagement.Employees.Default;
        CreatePolicyName = SwapMidPermissions.EnterpriseManagement.Employees.Create;
        UpdatePolicyName = SwapMidPermissions.EnterpriseManagement.Employees.Update;
        DeletePolicyName = SwapMidPermissions.EnterpriseManagement.Employees.Delete;
    }

    public override async Task<PagedResultDto<EmployeeDto>> GetListAsync(GetEmployeeListInput input)
    {
        var query = await Repository.GetQueryableAsync();
        var enterprise = await FindEnterpriseAsync(input.EnterpriseCode, input.EnterpriseId);

        if (HasEnterpriseFilter(input) && enterprise == null)
        {
            return new PagedResultDto<EmployeeDto>(0, new List<EmployeeDto>());
        }

        if (enterprise != null)
        {
            query = query.Where(x => x.EnterpriseId == enterprise.Id);
        }

        if (input.DepartmentId.HasValue)
        {
            if (input.IncludeChildDepartments)
            {
                var deptIds = await _departmentRepository.GetSelfAndDescendantIdsAsync(input.DepartmentId.Value);
                query = query.Where(x => x.DepartmentId.HasValue && deptIds.Contains(x.DepartmentId.Value));
            }
            else
            {
                query = query.Where(x => x.DepartmentId == input.DepartmentId.Value);
            }
        }
        else if (!string.IsNullOrWhiteSpace(input.DepartmentCode))
        {
            var departmentQuery = (await _departmentRepository.GetQueryableAsync())
                .Where(x => x.Code == input.DepartmentCode);

            if (enterprise != null)
            {
                departmentQuery = departmentQuery.Where(x => x.EnterpriseId == enterprise.Id);
            }

            var departmentIds = await AsyncExecuter.ToListAsync(departmentQuery.Select(x => x.Id));

            // 含下级：把每个匹配到的部门展开成其子树
            if (input.IncludeChildDepartments && departmentIds.Count > 0)
            {
                var expanded = new List<Guid>();
                foreach (var did in departmentIds)
                {
                    expanded.AddRange(await _departmentRepository.GetSelfAndDescendantIdsAsync(did));
                }
                departmentIds = expanded.Distinct().ToList();
            }

            query = query.Where(x => x.DepartmentId.HasValue && departmentIds.Contains(x.DepartmentId.Value));
        }

        if (input.Status.HasValue)
        {
            query = query.Where(x => x.Status == input.Status.Value);
        }

        // 按是否已生成登录账号筛选（UserId 绑定与否）：配合「在职 + 未生成账号」可定位待开号员工。
        if (input.HasAccount.HasValue)
        {
            query = input.HasAccount.Value
                ? query.Where(x => x.UserId != null)
                : query.Where(x => x.UserId == null);
        }

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filter = input.Filter.Trim();
            List<Guid>? pinyinIds = null;
            if (EmployeeNamePinyin.LooksLikeInitialsQuery(filter))
            {
                // 组织规模投影姓名即可；纯字母查询 SQL Contains 打不中「田烨」。
                var names = await AsyncExecuter.ToListAsync(query.Select(x => new { x.Id, x.Name }));
                pinyinIds = EmployeeNamePinyin.MatchIds(names.Select(item => (item.Id, (string?)item.Name)), filter);
            }

            query = pinyinIds is { Count: > 0 }
                ? query.Where(x =>
                    x.EmployeeNo.Contains(filter) ||
                    x.Name.Contains(filter) ||
                    (x.Email != null && x.Email.Contains(filter)) ||
                    (x.Phone != null && x.Phone.Contains(filter)) ||
                    pinyinIds.Contains(x.Id))
                : query.Where(x =>
                    x.EmployeeNo.Contains(filter) ||
                    x.Name.Contains(filter) ||
                    (x.Email != null && x.Email.Contains(filter)) ||
                    (x.Phone != null && x.Phone.Contains(filter)));
        }

        query = query.ApplyTimeRange(input.StartTime, input.EndTime, input.TimeType);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query
            .ApplyWhitelistedSorting(input.Sorting, SortWhitelist, "employeeno", logger: Logger)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));
        var dtos = items.Select(x => x.ToDto()).ToList();
        await FillNamesAsync(dtos, items);

        return new PagedResultDto<EmployeeDto>(totalCount, dtos);
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）：未命中回退工号，杜绝把用户串裸传动态排序（排序注入）。
    // 关联名列（企业/部门/岗位）需 join，不在白名单（前端对应列已禁排序）。
    // ⚠️ 审计列 creationtime / lastmodificationtime 必须在列（前端 withAuditColumns 自动追加这两列且默认可排序）：
    // 缺键 = 用户点「创建时间」列头后静默回退工号序，列头箭头与 URL 却显示已按创建时间排——界面与数据脱节。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<Employee>, bool, IOrderedQueryable<Employee>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<Employee>, bool, IOrderedQueryable<Employee>>>
        {
            ["employeeno"] = (q, d) => d ? q.OrderByDescending(x => x.EmployeeNo) : q.OrderBy(x => x.EmployeeNo),
            ["name"] = (q, d) => d ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name),
            ["email"] = (q, d) => d ? q.OrderByDescending(x => x.Email) : q.OrderBy(x => x.Email),
            ["phone"] = (q, d) => d ? q.OrderByDescending(x => x.Phone) : q.OrderBy(x => x.Phone),
            ["status"] = (q, d) => d ? q.OrderByDescending(x => x.Status) : q.OrderBy(x => x.Status),
            ["userid"] = (q, d) => d ? q.OrderByDescending(x => x.UserId) : q.OrderBy(x => x.UserId),
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
            ["lastmodificationtime"] = (q, d) => d ? q.OrderByDescending(x => x.LastModificationTime) : q.OrderBy(x => x.LastModificationTime),
        };

    public override async Task<EmployeeDto> GetAsync(Guid id)
    {
        var employee = await Repository.GetAsync(id);
        var dto = employee.ToDto();
        await FillNamesAsync(new List<EmployeeDto> { dto }, new List<Employee> { employee });
        return dto;
    }

    [Authorize(SwapMidPermissions.EnterpriseManagement.Employees.Create)]
    public override async Task<EmployeeDto> CreateAsync(EmployeeCreateUpdateDto input)
    {
        var references = await ResolveReferencesAsync(input);
        await ValidateUniqueAsync(input, references.EnterpriseId, excludeId: null);

        var employee = new Employee(
            GuidGenerator.Create(),
            references.EnterpriseId,
            input.EmployeeNo,
            input.Name,
            references.DepartmentId,
            input.Email,
            input.Phone,
            input.Position,
            input.UserId,
            references.PositionId);
        ApplyStatus(employee, input.Status);

        await Repository.InsertAsync(employee, autoSave: true);
        var dto = employee.ToDto();
        await FillNamesAsync(new List<EmployeeDto> { dto }, new List<Employee> { employee });
        return dto;
    }

    [Authorize(SwapMidPermissions.EnterpriseManagement.Employees.Update)]
    public override async Task<EmployeeDto> UpdateAsync(Guid id, EmployeeCreateUpdateDto input)
    {
        var employee = await Repository.GetAsync(id);
        var references = await ResolveReferencesAsync(input);

        if (references.EnterpriseId != employee.EnterpriseId)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.EnterpriseNotFound)
                .WithData("enterpriseId", references.EnterpriseId);
        }

        await ValidateUniqueAsync(input, references.EnterpriseId, id);

        employee.Update(input.EmployeeNo, input.Name, references.DepartmentId, input.Email, input.Phone, input.Position, references.PositionId);
        if (input.UserId.HasValue) employee.BindUser(input.UserId.Value); else employee.UnbindUser();
        ApplyStatus(employee, input.Status);

        await Repository.UpdateAsync(employee, autoSave: true);
        var dto = employee.ToDto();
        await FillNamesAsync(new List<EmployeeDto> { dto }, new List<Employee> { employee });
        return dto;
    }

    [Authorize(SwapMidPermissions.EnterpriseManagement.Employees.Delete)]
    public Task<BatchOperationResultDto> BatchDeleteAsync(BatchIdsInput input)
        => _batchOperationService.BatchDeleteAsync(input);

    [Authorize(SwapMidPermissions.EnterpriseManagement.Employees.Update)]
    public Task<BatchOperationResultDto> BatchSetStatusAsync(BatchSetEmployeeStatusInput input)
        => _batchOperationService.BatchSetStatusAsync(input);

    [Authorize(SwapMidPermissions.EnterpriseManagement.Employees.Update)]
    public Task<BatchAccountResultDto> BatchGenerateAccountsAsync(BatchGenerateAccountsInput input)
        => _batchOperationService.BatchGenerateAccountsAsync(input);

    [Authorize(SwapMidPermissions.EnterpriseManagement.Employees.Update)]
    public Task<BatchAccountResultDto> BulkGenerateAccountsAsync(BulkGenerateAccountsInput input)
        => _batchOperationService.BulkGenerateAccountsAsync(input);

    private async Task<EmployeeReferences> ResolveReferencesAsync(EmployeeCreateUpdateDto input)
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
            var department = await FindDepartmentByCodeAsync(enterprise.Id, input.DepartmentCode);
            if (department == null)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.DepartmentNotFound)
                    .WithData("departmentCode", input.DepartmentCode);
            }

            departmentId = department.Id;
        }

        if (departmentId.HasValue)
        {
            var department = await _departmentRepository.FindAsync(departmentId.Value);
            if (department == null || department.EnterpriseId != enterprise.Id)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.DepartmentNotFound)
                    .WithData("departmentId", departmentId.Value);
            }
        }

        var positionId = input.PositionId;
        if (!string.IsNullOrWhiteSpace(input.PositionCode))
        {
            var position = await FindPositionByCodeAsync(enterprise.Id, input.PositionCode);
            if (position == null)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.PositionNotFound)
                    .WithData("positionCode", input.PositionCode);
            }

            positionId = position.Id;
        }

        if (positionId.HasValue)
        {
            var position = await _positionRepository.FindAsync(positionId.Value);
            if (position == null || position.EnterpriseId != enterprise.Id)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.PositionNotFound)
                    .WithData("positionId", positionId.Value);
            }

            if (departmentId.HasValue && position.DepartmentId != departmentId.Value)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.PositionNotFound)
                    .WithData("positionId", positionId.Value)
                    .WithData("departmentId", departmentId.Value);
            }
        }

        return new EmployeeReferences(enterprise.Id, departmentId, positionId);
    }

    private async Task ValidateUniqueAsync(EmployeeCreateUpdateDto input, Guid enterpriseId, Guid? excludeId)
    {
        if (await _employeeRepository.EmployeeNoExistsAsync(enterpriseId, input.EmployeeNo, excludeId))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.EmployeeNoAlreadyExists)
                .WithData("employeeNo", input.EmployeeNo);
        }

        if (input.UserId.HasValue && await _employeeRepository.UserBindingExistsAsync(input.UserId.Value, excludeId))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.EmployeeUserAlreadyBound)
                .WithData("userId", input.UserId.Value);
        }
    }

    private static void ApplyStatus(Employee employee, EmployeeStatus status)
    {
        if (status == EmployeeStatus.Active)
        {
            employee.Activate();
            return;
        }

        if (status == EmployeeStatus.Suspended)
        {
            employee.Suspend();
            return;
        }

        employee.Resign();
    }

    private async Task FillNamesAsync(List<EmployeeDto> dtos, List<Employee> entities)
    {
        var enterpriseIds = entities.Select(x => x.EnterpriseId).Distinct().ToArray();
        var departmentIds = entities.Where(x => x.DepartmentId.HasValue).Select(x => x.DepartmentId!.Value).Distinct().ToArray();
        var positionIds = entities.Where(x => x.PositionId.HasValue).Select(x => x.PositionId!.Value).Distinct().ToArray();

        var enterprises = (await AsyncExecuter.ToListAsync((await _enterpriseRepository.GetQueryableAsync())
                .Where(x => enterpriseIds.Contains(x.Id))))
            .ToDictionary(x => x.Id);
        var departments = (await AsyncExecuter.ToListAsync((await _departmentRepository.GetQueryableAsync())
                .Where(x => departmentIds.Contains(x.Id))))
            .ToDictionary(x => x.Id);
        var positions = positionIds.Length > 0
            ? (await AsyncExecuter.ToListAsync((await _positionRepository.GetQueryableAsync())
                    .Where(x => positionIds.Contains(x.Id))))
                .ToDictionary(x => x.Id)
            : new Dictionary<Guid, Position>();

        foreach (var dto in dtos)
        {
            var enterprise = enterprises.GetValueOrDefault(dto.EnterpriseId);
            var department = dto.DepartmentId.HasValue ? departments.GetValueOrDefault(dto.DepartmentId.Value) : null;
            var position = dto.PositionId.HasValue ? positions.GetValueOrDefault(dto.PositionId.Value) : null;

            dto.EnterpriseCode = enterprise?.Code;
            dto.EnterpriseName = enterprise?.Name;
            dto.Enterprise = enterprise == null ? null : ToReference(enterprise);
            dto.DepartmentCode = department?.Code;
            dto.DepartmentName = department?.Name;
            dto.Department = department == null ? null : ToReference(department);
            dto.PositionCode = position?.Code;
            dto.PositionName = position?.Name;
            dto.PositionRef = position == null ? null : ToReference(position);
        }
    }

    private static bool HasEnterpriseFilter(GetEmployeeListInput input)
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

    private async Task<Position?> FindPositionByCodeAsync(Guid enterpriseId, string code)
    {
        return await AsyncExecuter.FirstOrDefaultAsync((await _positionRepository.GetQueryableAsync())
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

    private static EntityReferenceDto ToReference(Position position) => new()
    {
        Id = position.Id,
        Code = position.Code,
        Name = position.Name
    };

    private sealed record EmployeeReferences(Guid EnterpriseId, Guid? DepartmentId, Guid? PositionId);
}
