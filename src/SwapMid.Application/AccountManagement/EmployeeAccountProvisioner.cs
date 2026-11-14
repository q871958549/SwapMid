using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Settings;
using SwapMid.SharedKernel.Employees;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.SettingManagement;

namespace SwapMid.AccountManagement;

/// <summary>依据员工只读快照开通或禁用平台账号。</summary>
[ExposeServices(typeof(IEmployeeAccountProvisioner), typeof(EmployeeAccountProvisioner))]
public class EmployeeAccountProvisioner : IEmployeeAccountProvisioner, ITransientDependency
{
    // 通过领域服务 IdentityUserManager 创建账号（无 [Authorize]），确保 HTTP 与 Hangfire 均可调用。
    private readonly IdentityUserManager _userManager;
    private readonly IEmployeeDirectory _employeeDirectory;
    private readonly IEmployeeUserBinder _employeeUserBinder;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly ISettingManager _settingManager;
    private readonly IConfiguration _configuration;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;

    public EmployeeAccountProvisioner(
        IdentityUserManager userManager,
        IEmployeeDirectory employeeDirectory,
        IEmployeeUserBinder employeeUserBinder,
        IIdentityRoleRepository roleRepository,
        ISettingManager settingManager,
        IConfiguration configuration,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant)
    {
        _userManager = userManager;
        _employeeDirectory = employeeDirectory;
        _employeeUserBinder = employeeUserBinder;
        _roleRepository = roleRepository;
        _settingManager = settingManager;
        _configuration = configuration;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
    }

    /// <summary>
    /// 员工开通或绑定账号。指定角色仅在实际创建新账号时追加到默认角色集合；调用方必须先校验权限和角色名。
    /// </summary>
    public async Task<EmployeeAccountProvisionResult> ProvisionAsync(
        Guid employeeId,
        IReadOnlyCollection<string>? roleNames = null)
    {
        var employee = await _employeeDirectory.FindAsync(employeeId)
            ?? throw new UserFriendlyException("员工不存在");

        if (employee.UserId.HasValue)
        {
            var boundUser = await _userManager.FindByIdAsync(employee.UserId.Value.ToString());
            if (boundUser != null)
            {
                return ToResult(employee, boundUser, created: false, alreadyBound: true, "员工已绑定账号");
            }

            await _employeeUserBinder.UnbindAsync(employee.Id);
        }

        var existing = await _userManager.FindByNameAsync(employee.EmployeeNo);
        var created = false;
        if (existing == null)
        {
            existing = new IdentityUser(
                _guidGenerator.Create(),
                employee.EmployeeNo,
                BuildAccountEmail(employee),
                _currentTenant.Id)
            {
                Name = employee.Name,
            };

            if (!string.IsNullOrWhiteSpace(employee.Phone))
            {
                existing.SetPhoneNumber(employee.Phone, confirmed: false);
            }

            existing.SetIsActive(employee.Status == EmployeeStatus.Active);

            var createResult = await _userManager.CreateAsync(existing, await GetDefaultPasswordAsync());
            if (!createResult.Succeeded)
            {
                throw new UserFriendlyException("创建账号失败：" + string.Join("；", createResult.Errors.Select(error => error.Description)));
            }

            (await _userManager.AddDefaultRolesAsync(existing)).CheckErrors();

            if (roleNames is { Count: > 0 })
            {
                var defaultRoleNames = (await _roleRepository.GetListAsync())
                    .Where(role => role.IsDefault)
                    .Select(role => role.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var rolesToAdd = roleNames
                    .Where(name => !string.IsNullOrWhiteSpace(name) && !defaultRoleNames.Contains(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (rolesToAdd.Count > 0)
                {
                    (await _userManager.AddToRolesAsync(existing, rolesToAdd)).CheckErrors();
                }
            }

            if (await ShouldForceChangePasswordAsync())
            {
                existing.SetShouldChangePasswordOnNextLogin(true);
                (await _userManager.UpdateAsync(existing)).CheckErrors();
            }

            created = true;
        }

        await _employeeUserBinder.BindAsync(employee.Id, existing.Id);
        return ToResult(employee, existing, created, alreadyBound: false, created ? "账号已创建" : "已绑定已有账号");
    }

    /// <summary>禁用员工绑定账号。无账号、账号不存在或已禁用时返回 false。</summary>
    public async Task<bool> DisableAccountAsync(Guid employeeId)
    {
        var employee = await _employeeDirectory.FindAsync(employeeId);
        if (employee?.UserId is not Guid userId)
        {
            return false;
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null || !user.IsActive)
        {
            return false;
        }

        user.SetIsActive(false);
        (await _userManager.UpdateAsync(user)).CheckErrors();
        return true;
    }

    // 公开给 AccountUserAppService（管理员重置密码）复用同一份初始密码取值逻辑。
    public async Task<string> GetDefaultPasswordAsync()
    {
        return (await _settingManager.GetOrNullGlobalAsync(SwapMidSettings.AccountDefaultPassword)
            ?? _configuration["Account:DefaultPassword"]
            ?? "SwapMid@123456").Trim();
    }

    public async Task<bool> ShouldForceChangePasswordAsync()
        => (await _settingManager.GetOrNullGlobalAsync(
            SwapMidSettings.AccountForceChangePasswordOnFirstLogin) ?? "true") != "false";

    private static string BuildAccountEmail(EmployeeSnapshot employee)
        => EmployeeEmailHelper.ResolveEmail(employee.Email, employee.Name);

    private static EmployeeAccountProvisionResult ToResult(
        EmployeeSnapshot employee,
        IdentityUser user,
        bool created,
        bool alreadyBound,
        string message)
        => new(
            employee.Id,
            user.Id,
            user.UserName,
            created,
            alreadyBound,
            message);
}
