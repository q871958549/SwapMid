using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Permissions;
using SwapMid.SharedKernel;
using SwapMid.SharedKernel.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;

namespace SwapMid.EnterpriseManagement;

public class EmployeeBatchOperationService : ITransientDependency
{
    private readonly IRepository<Employee, Guid> _employeeRepository;
    private readonly IEmployeeAccountProvisioner _accountProvisioner;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly ILookupNormalizer _lookupNormalizer;
    private readonly IAuthorizationService _authorizationService;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public EmployeeBatchOperationService(
        IRepository<Employee, Guid> employeeRepository,
        IEmployeeAccountProvisioner accountProvisioner,
        IIdentityRoleRepository roleRepository,
        ILookupNormalizer lookupNormalizer,
        IAuthorizationService authorizationService,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _employeeRepository = employeeRepository;
        _accountProvisioner = accountProvisioner;
        _roleRepository = roleRepository;
        _lookupNormalizer = lookupNormalizer;
        _authorizationService = authorizationService;
        _asyncExecuter = asyncExecuter;
    }

    public Task<BatchOperationResultDto> BatchDeleteAsync(BatchIdsInput input)
        => BatchOperationExecutor.DeleteAsync(_employeeRepository, input.Ids);

    public Task<BatchOperationResultDto> BatchSetStatusAsync(BatchSetEmployeeStatusInput input)
        => BatchOperationExecutor.UpdateAsync(
            _employeeRepository,
            input.Ids,
            employee => ApplyStatus(employee, input.Status));

    public async Task<BatchAccountResultDto> BatchGenerateAccountsAsync(BatchGenerateAccountsInput input)
    {
        var roleNames = await ValidateRoleNamesAsync(input.RoleNames);

        var result = new BatchAccountResultDto { Requested = input.Ids.Count };
        foreach (var id in input.Ids.Distinct())
        {
            var employee = await _employeeRepository.FindAsync(id);
            if (employee == null)
            {
                result.Skipped++;
                continue;
            }

            if (employee.Status != EmployeeStatus.Active)
            {
                result.Skipped++;
                result.Messages.Add($"{employee.Name}（{employee.EmployeeNo}）非在职，已跳过");
                continue;
            }

            try
            {
                var provision = await _accountProvisioner.ProvisionAsync(employee.Id, roleNames);
                if (provision.AlreadyBound)
                {
                    result.AlreadyBound++;
                }
                else if (provision.Created)
                {
                    result.Created++;
                }
                else
                {
                    result.AlreadyBound++;
                }

                result.Succeeded++;
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Messages.Add($"{employee.Name}（{employee.EmployeeNo}）生成失败：{ex.Message}");
            }
        }

        return result;
    }

    public async Task<BatchAccountResultDto> BulkGenerateAccountsAsync(BulkGenerateAccountsInput input)
    {
        var roleNames = await ValidateRoleNamesAsync(input.RoleNames);

        var query = (await _employeeRepository.GetQueryableAsync())
            .Where(x => x.Status == EmployeeStatus.Active && x.UserId == null);
        if (input.EnterpriseId.HasValue)
        {
            query = query.Where(x => x.EnterpriseId == input.EnterpriseId.Value);
        }
        if (input.DepartmentId.HasValue)
        {
            query = query.Where(x => x.DepartmentId == input.DepartmentId.Value);
        }

        var employees = await _asyncExecuter.ToListAsync(query.OrderBy(x => x.EmployeeNo));
        var result = new BatchAccountResultDto { Requested = employees.Count };
        foreach (var employee in employees)
        {
            try
            {
                var provision = await _accountProvisioner.ProvisionAsync(employee.Id, roleNames);
                if (provision.Created)
                {
                    result.Created++;
                }
                else
                {
                    result.AlreadyBound++;
                }

                result.Succeeded++;
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Messages.Add($"{employee.Name}（{employee.EmployeeNo}）生成失败：{ex.Message}");
            }
        }

        return result;
    }

    private async Task<List<string>> ValidateRoleNamesAsync(List<string>? roleNames)
    {
        var distinctNames = (roleNames ?? new List<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctNames.Count == 0)
        {
            return distinctNames;
        }

        await _authorizationService.CheckAsync(SwapMidPermissions.AccountManagement.Users.Update);

        var rolesByNormalizedName = (await _roleRepository.GetListAsync())
            .ToDictionary(r => r.NormalizedName);
        var missingNames = distinctNames
            .Where(name => !rolesByNormalizedName.ContainsKey(_lookupNormalizer.NormalizeName(name)))
            .ToList();
        if (missingNames.Count > 0)
        {
            throw new UserFriendlyException($"以下角色不存在: {string.Join(",", missingNames)}");
        }

        var staticNames = distinctNames
            .Where(name => rolesByNormalizedName[_lookupNormalizer.NormalizeName(name)].IsStatic)
            .ToList();
        if (staticNames.Count > 0)
        {
            throw new UserFriendlyException($"系统内置角色不可经批量开通授予: {string.Join(",", staticNames)}");
        }

        return distinctNames;
    }

    private static void ApplyStatus(Employee employee, EmployeeStatus status)
    {
        switch (status)
        {
            case EmployeeStatus.Active:
                employee.Activate();
                break;
            case EmployeeStatus.Suspended:
                employee.Suspend();
                break;
            case EmployeeStatus.Resigned:
                employee.Resign();
                break;
        }
    }
}
