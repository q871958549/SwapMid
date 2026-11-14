using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using SwapMid.SharedKernel.Employees;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Xunit;

namespace SwapMid.EnterpriseManagement;

[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
public class EnterpriseManagementAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IEnterpriseAppService _enterpriseAppService;
    private readonly ICompanyDepartmentAppService _departmentAppService;
    private readonly IEmployeeAppService _employeeAppService;
    private readonly IIdentityRoleAppService _roleAppService;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IdentityUserManager _userManager;

    public EnterpriseManagementAppServiceTests()
    {
        _enterpriseAppService = GetRequiredService<IEnterpriseAppService>();
        _departmentAppService = GetRequiredService<ICompanyDepartmentAppService>();
        _employeeAppService = GetRequiredService<IEmployeeAppService>();
        _roleAppService = GetRequiredService<IIdentityRoleAppService>();
        _roleRepository = GetRequiredService<IIdentityRoleRepository>();
        _userManager = GetRequiredService<IdentityUserManager>();
    }

    [Fact]
    public async Task Should_Create_And_Filter_Enterprise()
    {
        var suffix = Guid.NewGuid().ToString("N");

        var created = await _enterpriseAppService.CreateAsync(new EnterpriseCreateUpdateDto
        {
            Code = $"ENT-{suffix}",
            Name = $"Acme Enterprise {suffix}",
            ShortName = "Acme",
            ContactPerson = "Admin",
            Status = EnterpriseStatus.Active
        });

        created.Id.ShouldNotBe(Guid.Empty);
        created.Code.ShouldBe($"ENT-{suffix}");
        created.IsEnabled.ShouldBeTrue();

        var list = await _enterpriseAppService.GetListAsync(new GetEnterpriseListInput
        {
            Filter = suffix,
            Status = EnterpriseStatus.Active,
            MaxResultCount = 10
        });

        list.TotalCount.ShouldBe(1);
        list.Items.Single().Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Should_Reject_Duplicate_Enterprise_Code()
    {
        var code = $"DUP-{Guid.NewGuid():N}";

        await _enterpriseAppService.CreateAsync(new EnterpriseCreateUpdateDto
        {
            Code = code,
            Name = "Duplicate Enterprise"
        });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _enterpriseAppService.CreateAsync(new EnterpriseCreateUpdateDto
            {
                Code = code,
                Name = "Duplicate Enterprise 2"
            }));

        exception.Code.ShouldBe(SwapMidDomainErrorCodes.EnterpriseManagement.EnterpriseCodeAlreadyExists);
    }

    [Fact]
    public async Task Should_Create_Department_And_Reject_Duplicate_Code_In_Same_Enterprise()
    {
        var enterprise = await CreateEnterpriseAsync();

        var department = await _departmentAppService.CreateAsync(new CompanyDepartmentCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            Code = "IT",
            Name = "Information Technology",
            Order = 10
        });

        department.Id.ShouldNotBe(Guid.Empty);
        department.EnterpriseName.ShouldBe(enterprise.Name);

        var list = await _departmentAppService.GetListAsync(new GetCompanyDepartmentListInput
        {
            EnterpriseId = enterprise.Id,
            Filter = "IT",
            MaxResultCount = 10
        });

        list.TotalCount.ShouldBe(1);
        list.Items.Single().EnterpriseName.ShouldBe(enterprise.Name);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _departmentAppService.CreateAsync(new CompanyDepartmentCreateUpdateDto
            {
                EnterpriseId = enterprise.Id,
                Code = "IT",
                Name = "IT Duplicate"
            }));

        exception.Code.ShouldBe(SwapMidDomainErrorCodes.EnterpriseManagement.DepartmentCodeAlreadyExists);
    }

    [Fact]
    public async Task Should_Create_And_Update_Department_Leader_Without_Wiping_Oa_Mapping()
    {
        var enterprise = await CreateEnterpriseAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var employee = await _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            EmployeeNo = $"LDR-{suffix}",
            Name = "部门负责人",
            Status = EmployeeStatus.Active,
        });

        var department = await _departmentAppService.CreateAsync(new CompanyDepartmentCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            Code = $"IT-{suffix}",
            Name = "IT部",

            LeaderEmployeeId = employee.Id,
        });

        department.LeaderEmployeeId.ShouldBe(employee.Id);
        department.LeaderEmployeeName.ShouldBe(employee.Name);


        var updated = await _departmentAppService.UpdateAsync(department.Id, new CompanyDepartmentCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            Code = $"IT-{suffix}",
            Name = "信息技术部",
            LeaderEmployeeId = null,
        });

        updated.Name.ShouldBe("信息技术部");
        updated.LeaderEmployeeId.ShouldBeNull();

        updated.Level.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Create_Employee_And_Reject_Duplicate_Employee_No()
    {
        var enterprise = await CreateEnterpriseAsync();
        var department = await _departmentAppService.CreateAsync(new CompanyDepartmentCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            Code = "HR",
            Name = "Human Resources"
        });

        var employee = await _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            DepartmentId = department.Id,
            EmployeeNo = "E001",
            Name = "Alice",
            Email = "alice@example.test",
            Status = EmployeeStatus.Active
        });

        employee.Id.ShouldNotBe(Guid.Empty);
        employee.EnterpriseName.ShouldBe(enterprise.Name);
        employee.DepartmentName.ShouldBe(department.Name);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
            {
                EnterpriseId = enterprise.Id,
                DepartmentId = department.Id,
                EmployeeNo = "E001",
                Name = "Bob"
            }));

        exception.Code.ShouldBe(SwapMidDomainErrorCodes.EnterpriseManagement.EmployeeNoAlreadyExists);
    }

    [Fact]
    public async Task Should_Reject_Duplicate_User_Binding()
    {
        var enterprise = await CreateEnterpriseAsync();
        var userId = Guid.NewGuid();

        await _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            EmployeeNo = "U001",
            Name = "Bound User 1",
            UserId = userId
        });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
            {
                EnterpriseId = enterprise.Id,
                EmployeeNo = "U002",
                Name = "Bound User 2",
                UserId = userId
            }));

        exception.Code.ShouldBe(SwapMidDomainErrorCodes.EnterpriseManagement.EmployeeUserAlreadyBound);
    }

    [Fact]
    public async Task GetListAsync_Should_Filter_By_Email_And_Phone()
    {
        var enterprise = await CreateEnterpriseAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"filter-{suffix}@example.test";
        var phone = $"186{suffix.PadRight(8, '0')[..8]}";
        await _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            EmployeeNo = $"F-{suffix}",
            Name = $"Filter{suffix}",
            Email = email,
            Phone = phone,
            Status = EmployeeStatus.Active,
        });

        var byEmail = await _employeeAppService.GetListAsync(new GetEmployeeListInput
        {
            EnterpriseId = enterprise.Id,
            Filter = email,
            MaxResultCount = 10,
        });
        byEmail.TotalCount.ShouldBe(1);
        byEmail.Items.Single().Email.ShouldBe(email);

        var byPhone = await _employeeAppService.GetListAsync(new GetEmployeeListInput
        {
            EnterpriseId = enterprise.Id,
            Filter = phone,
            MaxResultCount = 10,
        });
        byPhone.TotalCount.ShouldBe(1);
        byPhone.Items.Single().Phone.ShouldBe(phone);
    }

    // P2-L1：勾选行「批量生成账号」路径（BatchGenerateAccountsAsync）同样支持指定角色，
    // 语义与一键开通路径一致——账号角色 = 默认角色 ∪ 指定角色。
    [Fact]
    public async Task BatchGenerateAccountsAsync_Should_Grant_Default_Union_Specified_Roles()
    {
        var enterprise = await CreateEnterpriseAsync();
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

        var employee = await _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            EmployeeNo = $"PICK-{suffix}",
            Name = $"Pick{suffix}",
            Status = EmployeeStatus.Active,
        });

        var result = await _employeeAppService.BatchGenerateAccountsAsync(new BatchGenerateAccountsInput
        {
            Ids = new List<Guid> { employee.Id },
            RoleNames = new List<string> { extraRoleName },
        });

        result.Succeeded.ShouldBe(1);
        result.Created.ShouldBe(1);

        // 账号角色断言（userManager 读仓储须在活动 UoW 内，同 EmployeeAccountProvisionerTests 注记）。
        await WithUnitOfWorkAsync(async () =>
        {
            var reloaded = await _employeeAppService.GetAsync(employee.Id);
            reloaded.UserId.ShouldNotBeNull();

            var user = await _userManager.FindByIdAsync(reloaded.UserId!.Value.ToString());
            user.ShouldNotBeNull();
            var roles = await _userManager.GetRolesAsync(user!);
            roles.ShouldContain(defaultRoleName);
            roles.ShouldContain(extraRoleName);
            roles.Count.ShouldBe(2);
        });
    }

    // P2-L1：批量开通时指定角色——角色名有不存在的项须整批前置拒绝，不创建任何账号
    // （不进逐条容错；角色写错是配置错误，不该半批成功）。
    [Fact]
    public async Task BulkGenerateAccountsAsync_Should_Reject_Whole_Batch_When_RoleName_Missing()
    {
        var enterprise = await CreateEnterpriseAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var employee = await _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            EmployeeNo = $"BULK-{suffix}",
            Name = $"Bulk{suffix}",
            Status = EmployeeStatus.Active,
        });

        var missingRoleName = $"missing-role-{suffix}";
        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _employeeAppService.BulkGenerateAccountsAsync(new BulkGenerateAccountsInput
            {
                EnterpriseId = enterprise.Id,
                RoleNames = new List<string> { missingRoleName },
            }));

        exception.Message.ShouldContain(missingRoleName);

        // 零账号创建：校验失败须在查询/循环之前短路，员工仍未绑定账号。
        var reloaded = await _employeeAppService.GetAsync(employee.Id);
        reloaded.UserId.ShouldBeNull();
    }

    // 提权链收口（isStatic 保险）：roleNames 含系统内置角色（isStatic，即 admin）须整批前置拒绝、
    // 零账号创建——批量开通是低摩擦通道，不可借它授内置角色。自建 isStatic 角色而非锚种子 admin
    // （守 efcore-tests-flaky-isolation「断言只锚自建数据」纪律；IdentityRoleCreateDto 无 IsStatic
    // 字段，故经仓储直插）。
    // 另一重保险（动态权限门 AccountManagement.Users.Update，经 AuthorizationService.CheckAsync）
    // 在本测试环境无法覆盖拒绝路径——TestBaseModule 配置了 AddAlwaysAllowAuthorization()（与
    // ResumePushAppServiceTests 等既有测试注释同口径），该门的正确性由权限常量的编译期引用保证。
    [Fact]
    public async Task BulkGenerateAccountsAsync_Should_Reject_Whole_Batch_When_RoleName_IsStatic()
    {
        var enterprise = await CreateEnterpriseAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var staticRoleName = $"static-{suffix}";
        await WithUnitOfWorkAsync(() => _roleRepository.InsertAsync(
            new IdentityRole(Guid.NewGuid(), staticRoleName) { IsStatic = true }, autoSave: true));

        var employee = await _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            EmployeeNo = $"STAT-{suffix}",
            Name = $"Stat{suffix}",
            Status = EmployeeStatus.Active,
        });

        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _employeeAppService.BulkGenerateAccountsAsync(new BulkGenerateAccountsInput
            {
                EnterpriseId = enterprise.Id,
                RoleNames = new List<string> { staticRoleName },
            }));

        exception.Message.ShouldContain("系统内置角色");
        exception.Message.ShouldContain(staticRoleName);

        // 零账号创建：校验失败短路于查询/循环之前，员工仍未绑定账号。
        var reloaded = await _employeeAppService.GetAsync(employee.Id);
        reloaded.UserId.ShouldBeNull();
    }

    private Task<EnterpriseDto> CreateEnterpriseAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return _enterpriseAppService.CreateAsync(new EnterpriseCreateUpdateDto
        {
            Code = $"ENT-{suffix}",
            Name = $"Enterprise {suffix}"
        });
    }
}
