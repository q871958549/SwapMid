using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.SharedKernel.Employees;
using Volo.Abp.Application.Dtos;
using Volo.Abp.DependencyInjection;

namespace SwapMid.AccountManagement;

/// <summary>账号 DTO 的员工与组织只读投影。</summary>
public class AccountUserEmployeeBindingResolver : ITransientDependency
{
    private readonly IEmployeeDirectory _employeeDirectory;
    private readonly IOrganizationDirectory _organizationDirectory;

    public AccountUserEmployeeBindingResolver(
        IEmployeeDirectory employeeDirectory,
        IOrganizationDirectory organizationDirectory)
    {
        _employeeDirectory = employeeDirectory;
        _organizationDirectory = organizationDirectory;
    }

    /// <summary>单条账号 DTO 回填绑定员工基础信息。</summary>
    public async Task FillBindingAsync(AccountUserDto dto)
    {
        var employee = await _employeeDirectory.FindByUserIdAsync(dto.Id);
        if (employee == null)
        {
            return;
        }

        ApplyEmployee(dto, employee);
        dto.BoundEnterpriseName = await _organizationDirectory.GetEnterpriseNameAsync(employee.EnterpriseId);
        if (employee.DepartmentId.HasValue)
        {
            dto.BoundDepartmentName = (await _organizationDirectory.FindDepartmentAsync(employee.DepartmentId.Value))?.Name;
        }
    }

    /// <summary>当页批量回填；员工、企业与部门分别一次批量读取，查询数不随账号行数增长。</summary>
    public async Task FillBindingsAsync(List<AccountUserDto> dtos)
    {
        if (dtos.Count == 0)
        {
            return;
        }

        var employees = await _employeeDirectory.GetListByUserIdsAsync(dtos.Select(dto => dto.Id));
        var employeeByUserId = employees
            .Where(employee => employee.UserId.HasValue)
            .GroupBy(employee => employee.UserId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
        var enterpriseNames = await _organizationDirectory.GetEnterpriseNamesAsync(
            employees.Select(employee => employee.EnterpriseId));
        var departmentNames = (await _organizationDirectory.GetDepartmentListAsync())
            .ToDictionary(department => department.Id, department => department.Name);

        foreach (var dto in dtos)
        {
            if (!employeeByUserId.TryGetValue(dto.Id, out var employee))
            {
                continue;
            }

            ApplyEmployee(dto, employee);
            dto.BoundEnterpriseName = enterpriseNames.GetValueOrDefault(employee.EnterpriseId);
            if (employee.DepartmentId.HasValue)
            {
                dto.BoundDepartmentName = departmentNames.GetValueOrDefault(employee.DepartmentId.Value);
            }
        }
    }

    /// <summary>账号页绑定员工 Popover 的轻量详情，不包含人事敏感档案。</summary>
    public async Task<BoundEmployeeInfoDto?> GetBoundEmployeeInfoAsync(Guid userId)
    {
        var employee = await _employeeDirectory.FindByUserIdAsync(userId);
        if (employee == null)
        {
            return null;
        }

        return new BoundEmployeeInfoDto
        {
            EmployeeId = employee.Id,
            EmployeeNo = employee.EmployeeNo,
            Name = employee.Name,
            EnterpriseName = await _organizationDirectory.GetEnterpriseNameAsync(employee.EnterpriseId),
            DepartmentName = employee.DepartmentId.HasValue
                ? (await _organizationDirectory.FindDepartmentAsync(employee.DepartmentId.Value))?.Name
                : null,
            PositionName = employee.PositionId.HasValue
                ? await _organizationDirectory.GetPositionNameAsync(employee.PositionId.Value)
                : null,
            Position = employee.PositionText,
            Email = employee.Email,
            Phone = employee.Phone,
            Status = employee.Status,
        };
    }

    /// <summary>账号绑定表单专用在职员工候选选项（批量映射企业、部门与岗位名称）。</summary>
    public async Task<ListResultDto<AccountEmployeeOptionDto>> GetEmployeeOptionsAsync()
    {
        var employees = await _employeeDirectory.GetActiveListAsync();
        if (employees.Count == 0)
        {
            return new ListResultDto<AccountEmployeeOptionDto>();
        }

        var enterpriseNames = await _organizationDirectory.GetEnterpriseNamesAsync(
            employees.Select(employee => employee.EnterpriseId));
        var departmentNames = (await _organizationDirectory.GetDepartmentListAsync())
            .ToDictionary(department => department.Id, department => department.Name);
        var positionNames = await _organizationDirectory.GetPositionNamesAsync(
            employees.Where(employee => employee.PositionId.HasValue).Select(employee => employee.PositionId!.Value));

        var items = employees
            .OrderBy(employee => employee.EmployeeNo, StringComparer.OrdinalIgnoreCase)
            .Select(employee => new AccountEmployeeOptionDto
            {
                Id = employee.Id,
                UserId = employee.UserId,
                EmployeeNo = employee.EmployeeNo,
                Name = employee.Name,
                EnterpriseId = employee.EnterpriseId,
                EnterpriseName = enterpriseNames.GetValueOrDefault(employee.EnterpriseId),
                DepartmentId = employee.DepartmentId,
                DepartmentName = employee.DepartmentId.HasValue
                    ? departmentNames.GetValueOrDefault(employee.DepartmentId.Value)
                    : null,
                Email = employee.Email,
                Phone = employee.Phone,
                Position = employee.PositionId.HasValue
                    ? positionNames.GetValueOrDefault(employee.PositionId.Value) ?? employee.PositionText
                    : employee.PositionText,
                Status = employee.Status,
            })
            .ToList();

        return new ListResultDto<AccountEmployeeOptionDto>(items);
    }

    /// <summary>账号管理页部门筛选下拉专用轻量部门列表。</summary>
    public async Task<ListResultDto<AccountDepartmentOptionDto>> GetDepartmentOptionsAsync()
    {
        var departments = await _organizationDirectory.GetDepartmentListAsync();
        if (departments.Count == 0)
        {
            return new ListResultDto<AccountDepartmentOptionDto>();
        }

        var enterpriseNames = await _organizationDirectory.GetEnterpriseNamesAsync(
            departments.Select(department => department.EnterpriseId));

        var items = departments
            .OrderBy(department => department.Name, StringComparer.OrdinalIgnoreCase)
            .Select(department => new AccountDepartmentOptionDto
            {
                Id = department.Id,
                Name = department.Name,
                EnterpriseName = enterpriseNames.GetValueOrDefault(department.EnterpriseId),
            })
            .ToList();

        return new ListResultDto<AccountDepartmentOptionDto>(items);
    }

    private static void ApplyEmployee(AccountUserDto dto, EmployeeSnapshot employee)
    {
        dto.BoundEmployeeId = employee.Id;
        dto.BoundEmployeeNo = employee.EmployeeNo;
        dto.BoundEmployeeName = employee.Name;
        dto.BoundEnterpriseId = employee.EnterpriseId;
        dto.BoundDepartmentId = employee.DepartmentId;
    }
}
