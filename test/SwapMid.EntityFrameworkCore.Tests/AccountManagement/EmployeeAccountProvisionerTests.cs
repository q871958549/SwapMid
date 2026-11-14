using System;
using System.Threading.Tasks;
using SwapMid.EnterpriseManagement;
using SwapMid.EntityFrameworkCore;
using SwapMid.SharedKernel.Employees;
using Shouldly;
using Volo.Abp.Identity;
using Xunit;

namespace SwapMid.AccountManagement;

[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
public class EmployeeAccountProvisionerTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly EmployeeAccountProvisioner _provisioner;
    private readonly IEnterpriseAppService _enterpriseAppService;
    private readonly IEmployeeAppService _employeeAppService;
    private readonly IIdentityRoleAppService _roleAppService;
    private readonly IdentityUserManager _userManager;

    public EmployeeAccountProvisionerTests()
    {
        _provisioner = GetRequiredService<EmployeeAccountProvisioner>();
        _enterpriseAppService = GetRequiredService<IEnterpriseAppService>();
        _employeeAppService = GetRequiredService<IEmployeeAppService>();
        _roleAppService = GetRequiredService<IIdentityRoleAppService>();
        _userManager = GetRequiredService<IdentityUserManager>();
    }

    [Fact]
    public void SharedKernel_Employee_Services_Should_Be_Resolvable_By_Interface()
    {
        GetRequiredService<IEmployeeDirectory>().ShouldBeOfType<EmployeeDirectory>();
        GetRequiredService<IEmployeeUserBinder>().ShouldBeOfType<EmployeeUserBinder>();
        GetRequiredService<IOrganizationDirectory>().ShouldBeOfType<OrganizationDirectory>();
        GetRequiredService<IEmployeeAccountProvisioner>().ShouldBeOfType<EmployeeAccountProvisioner>();
    }

    [Fact]
    public async Task ProvisionAsync_Should_Assign_Default_Roles()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var defaultRoleName = $"def-{suffix}";
        await _roleAppService.CreateAsync(new IdentityRoleCreateDto
        {
            Name = defaultRoleName,
            IsDefault = true,
            IsPublic = true,
        });

        var employee = await CreateEmployeeAsync(suffix);

        await WithUnitOfWorkAsync(async () =>
        {
            var result = await _provisioner.ProvisionAsync(employee.Id);
            result.Created.ShouldBeTrue();

            var user = await _userManager.FindByIdAsync(result.UserId.ToString()!);
            user.ShouldNotBeNull();
            var roles = await _userManager.GetRolesAsync(user!);
            roles.ShouldContain(defaultRoleName);
        });
    }

    [Fact]
    public async Task ProvisionAsync_Should_Union_Specified_Roles_With_Default_Roles()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var defaultRoleName = $"def-{suffix}";
        var extraRoleName = $"extra-{suffix}";
        await _roleAppService.CreateAsync(new IdentityRoleCreateDto
        {
            Name = defaultRoleName,
            IsDefault = true,
            IsPublic = true,
        });
        await _roleAppService.CreateAsync(new IdentityRoleCreateDto
        {
            Name = extraRoleName,
            IsDefault = false,
            IsPublic = true,
        });

        var employee = await CreateEmployeeAsync(suffix);

        await WithUnitOfWorkAsync(async () =>
        {
            var result = await _provisioner.ProvisionAsync(employee.Id, new[] { extraRoleName, defaultRoleName });
            result.Created.ShouldBeTrue();

            var user = await _userManager.FindByIdAsync(result.UserId.ToString()!);
            user.ShouldNotBeNull();
            var roles = await _userManager.GetRolesAsync(user!);
            roles.ShouldContain(defaultRoleName);
            roles.ShouldContain(extraRoleName);
            roles.Count.ShouldBe(2);
        });
    }

    [Fact]
    public async Task DisableAccountAsync_Should_Deactivate_And_Be_Idempotent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var employee = await CreateEmployeeAsync(suffix);

        await WithUnitOfWorkAsync(async () =>
        {
            var provision = await _provisioner.ProvisionAsync(employee.Id);
            var user = await _userManager.FindByIdAsync(provision.UserId.ToString()!);
            user!.IsActive.ShouldBeTrue();

            var disabled = await _provisioner.DisableAccountAsync(employee.Id);
            disabled.ShouldBeTrue();

            var refreshed = await _userManager.FindByIdAsync(provision.UserId.ToString()!);
            refreshed!.IsActive.ShouldBeFalse();

            var again = await _provisioner.DisableAccountAsync(employee.Id);
            again.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task ProvisionAsync_Should_Preserve_Administrator_Disabled_Account()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var employee = await CreateEmployeeAsync(suffix);
        Guid userId = Guid.Empty;
        await WithUnitOfWorkAsync(async () =>
        {
            userId = (await _provisioner.ProvisionAsync(employee.Id)).UserId!.Value;
        });

        await GetRequiredService<IAccountUserAppService>().SetActiveAsync(userId, false);

        await WithUnitOfWorkAsync(async () =>
        {
            var again = await _provisioner.ProvisionAsync(employee.Id);
            again.AlreadyBound.ShouldBeTrue();
            (await _userManager.FindByIdAsync(userId.ToString()))!.IsActive.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task ProvisionAsync_Should_Fallback_To_Default_Email_When_Employee_Email_Is_Empty()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var enterprise = await _enterpriseAppService.CreateAsync(new EnterpriseCreateUpdateDto
        {
            Code = $"ENT-{suffix}",
            Name = $"Enterprise {suffix}",
        });
        var employee = await _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            EmployeeNo = $"E-{suffix}",
            Name = $"Emp{suffix}",
            Email = null,
            Status = EmployeeStatus.Active,
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var result = await _provisioner.ProvisionAsync(employee.Id);
            result.Created.ShouldBeTrue();

            var user = await _userManager.FindByIdAsync(result.UserId.ToString()!);
            user!.Email.ShouldBe(EmployeeEmailHelper.ResolveEmail(null, employee.Name));
        });
    }

    private async Task<EmployeeDto> CreateEmployeeAsync(string suffix, string? email = null, string? phone = null)
    {
        var enterprise = await _enterpriseAppService.CreateAsync(new EnterpriseCreateUpdateDto
        {
            Code = $"ENT-{suffix}",
            Name = $"Enterprise {suffix}",
        });
        return await _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            EmployeeNo = $"E-{suffix}",
            Name = $"Emp{suffix}",
            Email = email ?? $"emp-{suffix}@example.test",
            Phone = phone,
            Status = EmployeeStatus.Active,
        });
    }
}
