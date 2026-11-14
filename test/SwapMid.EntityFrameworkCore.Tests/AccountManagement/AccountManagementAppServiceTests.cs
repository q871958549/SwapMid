using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EnterpriseManagement;
using SwapMid.EntityFrameworkCore;
using SwapMid.Permissions;
using SwapMid.SharedKernel.Employees;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AuditLogging;
using Volo.Abp.Data;
using Xunit;

namespace SwapMid.AccountManagement;

[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
public class AccountManagementAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IAccountRoleAppService _roleAppService;
    private readonly IAccountUserAppService _userAppService;
    private readonly IAccountPermissionAppService _permissionAppService;
    private readonly ICurrentUserPermissionAppService _currentUserPermissionAppService;
    private readonly IEnterpriseAppService _enterpriseAppService;
    private readonly IEmployeeAppService _employeeAppService;
    private readonly ICompanyDepartmentAppService _departmentAppService;
    private readonly IAuditLogRepository _auditLogRepository;

    public AccountManagementAppServiceTests()
    {
        _roleAppService = GetRequiredService<IAccountRoleAppService>();
        _userAppService = GetRequiredService<IAccountUserAppService>();
        _permissionAppService = GetRequiredService<IAccountPermissionAppService>();
        _currentUserPermissionAppService = GetRequiredService<ICurrentUserPermissionAppService>();
        _enterpriseAppService = GetRequiredService<IEnterpriseAppService>();
        _employeeAppService = GetRequiredService<IEmployeeAppService>();
        _departmentAppService = GetRequiredService<ICompanyDepartmentAppService>();
        _auditLogRepository = GetRequiredService<IAuditLogRepository>();
    }

    [Fact]
    public async Task Should_Create_User_With_Role_And_Show_Employee_Binding()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var roleName = $"integration-role-{suffix}";
        var userName = $"integration-user-{suffix}";

        await _roleAppService.CreateAsync(new AccountRoleCreateUpdateDto
        {
            Name = roleName,
            IsPublic = true
        });

        var user = await _userAppService.CreateAsync(new AccountUserCreateDto
        {
            UserName = userName,
            Email = $"{userName}@example.test",
            Password = "Swap123!!",
            IsActive = true,
            RoleNames = { roleName }
        });

        user.Id.ShouldNotBe(Guid.Empty);
        user.RoleNames.ShouldContain(roleName);

        var enterprise = await _enterpriseAppService.CreateAsync(new EnterpriseCreateUpdateDto
        {
            Code = $"ENT-{suffix}",
            Name = $"Enterprise {suffix}"
        });

        var employee = await _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            UserId = user.Id,
            EmployeeNo = $"E-{suffix[..8]}",
            Name = "Bound Employee",
            Status = EmployeeStatus.Active
        });

        var loaded = await _userAppService.GetAsync(user.Id);

        loaded.BoundEmployeeId.ShouldBe(employee.Id);
        loaded.BoundEmployeeName.ShouldBe(employee.Name);
        loaded.BoundEnterpriseName.ShouldBe(enterprise.Name);

        var list = await _userAppService.GetListAsync(new GetAccountUserListInput
        {
            Filter = userName,
            MaxResultCount = 10
        });

        list.Items.Single().BoundEmployeeId.ShouldBe(employee.Id);
        list.Items.Single().RoleNames.ShouldContain(roleName);
    }

    [Fact]
    public async Task GetListAsync_Should_Match_Display_Name_By_Pinyin_Initials()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = await _userAppService.CreateAsync(new AccountUserCreateDto
        {
            UserName = $"pinyin-{suffix}",
            Name = "唐远",
            Email = $"pinyin-{suffix}@example.test",
            Password = "Swap123!!",
            IsActive = true
        });

        var result = await _userAppService.GetListAsync(new GetAccountUserListInput
        {
            Filter = "ty",
            MaxResultCount = 50
        });

        result.Items.ShouldContain(item => item.Id == user.Id);
    }

    [Fact]
    public async Task SetActiveAsync_Should_Toggle_IsActive()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = await _userAppService.CreateAsync(new AccountUserCreateDto
        {
            UserName = $"active-{suffix}",
            Email = $"active-{suffix}@example.test",
            Password = "Swap123!!",
            IsActive = true
        });

        await _userAppService.SetActiveAsync(user.Id, false);
        (await _userAppService.GetAsync(user.Id)).IsActive.ShouldBeFalse();

        await _userAppService.SetActiveAsync(user.Id, true);
        (await _userAppService.GetAsync(user.Id)).IsActive.ShouldBeTrue();
    }

    // 真·服务端分页：TotalCount 不随每页条数变化、两页不重叠（旧实现「先取 1000 再内存分页」破千会截断）。
    [Fact]
    public async Task GetListAsync_Should_Apply_ServerSide_Paging()
    {
        var prefix = $"page-{Guid.NewGuid():N}"[..16];
        for (var i = 0; i < 3; i++)
        {
            await _userAppService.CreateAsync(new AccountUserCreateDto
            {
                UserName = $"{prefix}-{i}",
                Email = $"{prefix}-{i}@example.test",
                Password = "Swap123!!",
                IsActive = true
            });
        }

        var firstPage = await _userAppService.GetListAsync(new GetAccountUserListInput
        {
            Filter = prefix,
            SkipCount = 0,
            MaxResultCount = 2
        });
        firstPage.TotalCount.ShouldBe(3);
        firstPage.Items.Count.ShouldBe(2);

        var secondPage = await _userAppService.GetListAsync(new GetAccountUserListInput
        {
            Filter = prefix,
            SkipCount = 2,
            MaxResultCount = 2
        });
        secondPage.TotalCount.ShouldBe(3);
        secondPage.Items.Count.ShouldBe(1);

        firstPage.Items.Select(x => x.Id)
            .Intersect(secondPage.Items.Select(x => x.Id))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task GetListAsync_Should_Fill_RoleNames_For_Each_Page_User()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleA = $"batch-role-a-{suffix}";
        var roleB = $"batch-role-b-{suffix}";
        await _roleAppService.CreateAsync(new AccountRoleCreateUpdateDto { Name = roleA, IsPublic = true });
        await _roleAppService.CreateAsync(new AccountRoleCreateUpdateDto { Name = roleB, IsPublic = true });

        var userA = await _userAppService.CreateAsync(new AccountUserCreateDto
        {
            UserName = $"batch-a-{suffix}",
            Email = $"batch-a-{suffix}@example.test",
            Password = "Swap123!!",
            IsActive = true,
            RoleNames = { roleA }
        });
        var userB = await _userAppService.CreateAsync(new AccountUserCreateDto
        {
            UserName = $"batch-b-{suffix}",
            Email = $"batch-b-{suffix}@example.test",
            Password = "Swap123!!",
            IsActive = true,
            RoleNames = { roleB }
        });

        var result = await _userAppService.GetListAsync(new GetAccountUserListInput
        {
            Filter = suffix,
            MaxResultCount = 50
        });

        result.Items.Single(x => x.Id == userA.Id).RoleNames.ShouldContain(roleA);
        result.Items.Single(x => x.Id == userA.Id).RoleNames.ShouldNotContain(roleB);
        result.Items.Single(x => x.Id == userB.Id).RoleNames.ShouldContain(roleB);
        result.Items.Single(x => x.Id == userB.Id).RoleNames.ShouldNotContain(roleA);
    }


    // 部门反向筛选：账号→员工反向关联，按部门(含下级)筛出绑定该部门员工的账号。
    [Fact]
    public async Task GetListAsync_Should_Filter_By_Department()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = await _userAppService.CreateAsync(new AccountUserCreateDto
        {
            UserName = $"dept-user-{suffix}",
            Email = $"dept-user-{suffix}@example.test",
            Password = "Swap123!!",
            IsActive = true
        });
        var enterprise = await _enterpriseAppService.CreateAsync(new EnterpriseCreateUpdateDto
        {
            Code = $"DENT-{suffix}",
            Name = $"Dept Enterprise {suffix}"
        });
        var department = await _departmentAppService.CreateAsync(new CompanyDepartmentCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            Code = $"DEP-{suffix}",
            Name = $"Dept {suffix}"
        });
        await _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            DepartmentId = department.Id,
            UserId = user.Id,
            EmployeeNo = $"D-{suffix}",
            Name = "Dept Bound",
            Status = EmployeeStatus.Active
        });

        var result = await _userAppService.GetListAsync(new GetAccountUserListInput
        {
            DepartmentId = department.Id,
            IncludeChildDepartments = true,
            MaxResultCount = 50
        });

        // 全新部门只绑了这一个账号：恰好命中、且无误筛。
        result.TotalCount.ShouldBe(1);
        result.Items.Single().Id.ShouldBe(user.Id);
        result.Items.Single().BoundDepartmentId.ShouldBe(department.Id);
    }

    // 共享内核阶段 2a：部门过滤必须包含根+子部门、不得按员工状态过滤，并保持 SQL 侧分页语义。
    [Fact]
    public async Task GetListAsync_Should_Filter_Bound_Users_In_Department_Tree_Regardless_Of_Employee_Status()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var enterprise = await _enterpriseAppService.CreateAsync(new EnterpriseCreateUpdateDto
        {
            Code = $"TREE-{suffix}",
            Name = $"Tree Enterprise {suffix}"
        });
        var root = await _departmentAppService.CreateAsync(new CompanyDepartmentCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            Code = $"ROOT-{suffix}",
            Name = $"Root {suffix}"
        });
        var child = await _departmentAppService.CreateAsync(new CompanyDepartmentCreateUpdateDto
        {
            EnterpriseId = enterprise.Id,
            ParentId = root.Id,
            Code = $"CHILD-{suffix}",
            Name = $"Child {suffix}"
        });

        var expectedUserIds = new List<Guid>();
        foreach (var (status, departmentId, label) in new[]
        {
            (EmployeeStatus.Active, root.Id, "active"),
            (EmployeeStatus.Suspended, child.Id, "suspended"),
            (EmployeeStatus.Resigned, child.Id, "resigned"),
        })
        {
            var userName = $"tree-{label}-{suffix}";
            var user = await _userAppService.CreateAsync(new AccountUserCreateDto
            {
                UserName = userName,
                Email = $"{userName}@example.test",
                Password = "Swap123!!",
                IsActive = true
            });
            expectedUserIds.Add(user.Id);
            await _employeeAppService.CreateAsync(new EmployeeCreateUpdateDto
            {
                EnterpriseId = enterprise.Id,
                DepartmentId = departmentId,
                UserId = user.Id,
                EmployeeNo = $"{label[..1].ToUpperInvariant()}-{suffix}",
                Name = $"{label} employee",
                Status = status
            });
        }

        var firstPage = await _userAppService.GetListAsync(new GetAccountUserListInput
        {
            DepartmentId = root.Id,
            IncludeChildDepartments = true,
            SkipCount = 0,
            MaxResultCount = 2,
            Sorting = "username"
        });
        var secondPage = await _userAppService.GetListAsync(new GetAccountUserListInput
        {
            DepartmentId = root.Id,
            IncludeChildDepartments = true,
            SkipCount = 2,
            MaxResultCount = 2,
            Sorting = "username"
        });

        firstPage.TotalCount.ShouldBe(3);
        secondPage.TotalCount.ShouldBe(3);
        firstPage.Items.Count.ShouldBe(2);
        secondPage.Items.Count.ShouldBe(1);
        firstPage.Items.Select(item => item.Id)
            .Concat(secondPage.Items.Select(item => item.Id))
            .ShouldBe(expectedUserIds, ignoreOrder: true);
    }

    // 角色分配弹窗轻量接口：一次拉全部账号，角色名经「遍历角色取成员」批量映射。
    [Fact]
    public async Task GetAllForAssignmentAsync_Should_Map_Role_Names()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = $"lite-role-{suffix}";
        var userName = $"lite-user-{suffix}";

        await _roleAppService.CreateAsync(new AccountRoleCreateUpdateDto { Name = roleName, IsPublic = true });
        var user = await _userAppService.CreateAsync(new AccountUserCreateDto
        {
            UserName = userName,
            Email = $"{userName}@example.test",
            Password = "Swap123!!",
            IsActive = true,
            RoleNames = { roleName }
        });

        var all = await _userAppService.GetAllForAssignmentAsync();

        var row = all.Items.FirstOrDefault(x => x.Id == user.Id);
        row.ShouldNotBeNull();
        row!.UserName.ShouldBe(userName);
        row.RoleNames.ShouldContain(roleName);
    }

    // P1-1:角色筛选下推到 IdentityUser.Roles 导航集合查询(EF 可译)，非内存过滤；与其余筛选条件正交。
    [Fact]
    public async Task GetListAsync_Should_Filter_By_RoleId()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = $"roleid-role-{suffix}";
        var role = await _roleAppService.CreateAsync(new AccountRoleCreateUpdateDto { Name = roleName, IsPublic = true });

        var memberUserName = $"roleid-member-{suffix}";
        var member = await _userAppService.CreateAsync(new AccountUserCreateDto
        {
            UserName = memberUserName,
            Email = $"{memberUserName}@example.test",
            Password = "Swap123!!",
            IsActive = true,
            RoleNames = { roleName }
        });

        // 同批建一个不持有该角色的账号,验证筛选确实排它、不是恰好全表都命中。
        await _userAppService.CreateAsync(new AccountUserCreateDto
        {
            UserName = $"roleid-nonmember-{suffix}",
            Email = $"roleid-nonmember-{suffix}@example.test",
            Password = "Swap123!!",
            IsActive = true
        });

        var result = await _userAppService.GetListAsync(new GetAccountUserListInput
        {
            RoleId = role.Id,
            MaxResultCount = 50
        });

        // 全新角色只有这一个成员：恰好命中、且无误筛非成员。
        result.TotalCount.ShouldBe(1);
        result.Items.Single().Id.ShouldBe(member.Id);
    }

    [Fact]
    public async Task GetListAsync_Should_Match_Any_Selected_Role_And_Active_Flag()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var finance = await _roleAppService.CreateAsync(new AccountRoleCreateUpdateDto { Name = $"roles-finance-{suffix}", IsPublic = true });
        var purchase = await _roleAppService.CreateAsync(new AccountRoleCreateUpdateDto { Name = $"roles-purchase-{suffix}", IsPublic = true });

        var financeUser = await CreateListedUserAsync($"roles-fin-{suffix}", finance.Name, isActive: true);
        var purchaseUser = await CreateListedUserAsync($"roles-pur-{suffix}", purchase.Name, isActive: false);
        await CreateListedUserAsync($"roles-none-{suffix}", roleName: null, isActive: true);

        var financeOnly = await _userAppService.GetListAsync(new GetAccountUserListInput
        {
            RoleId = finance.Id,
            MaxResultCount = 50,
        });
        financeOnly.Items.Single().Id.ShouldBe(financeUser.Id);

        var activePurchase = await _userAppService.GetListAsync(new GetAccountUserListInput
        {
            RoleId = purchase.Id,
            IsActive = true,
            MaxResultCount = 50,
        });
        activePurchase.Items.ShouldBeEmpty();
    }

    private async Task<AccountUserDto> CreateListedUserAsync(string userName, string? roleName, bool isActive)
    {
        return await _userAppService.CreateAsync(new AccountUserCreateDto
        {
            UserName = userName,
            Email = $"{userName}@example.test",
            Password = "Swap123!!",
            IsActive = isActive,
            RoleNames = roleName == null ? new List<string>() : new List<string> { roleName },
        });
    }

    // P1-4:任意用户有效权限查询——角色授予的权限应出现在该用户的有效权限里，来源标注为 R+角色名
    // (验证 IPermissionAppService.GetAsync("U", userId) 按 providerKey 反查目标用户角色而非当前登录者角色)。
    [Fact]
    public async Task GetForUserAsync_Should_Report_Granted_Permission_With_Role_Provider()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = $"effperm-role-{suffix}";
        await _roleAppService.CreateAsync(new AccountRoleCreateUpdateDto { Name = roleName, IsPublic = true });

        var userName = $"effperm-user-{suffix}";
        var user = await _userAppService.CreateAsync(new AccountUserCreateDto
        {
            UserName = userName,
            Email = $"{userName}@example.test",
            Password = "Swap123!!",
            IsActive = true,
            RoleNames = { roleName }
        });

        await _permissionAppService.UpdateRolePermissionsAsync(roleName, new AccountPermissionUpdateDto
        {
            Permissions =
            {
                new AccountPermissionGrantDto
                {
                    Name = SwapMidPermissions.AccountManagement.Users.Default,
                    IsGranted = true
                }
            }
        });

        var effective = await _currentUserPermissionAppService.GetForUserAsync(user.Id);

        var permission = effective.Groups
            .SelectMany(g => g.Permissions)
            .FirstOrDefault(p => p.Name == SwapMidPermissions.AccountManagement.Users.Default);

        permission.ShouldNotBeNull();
        permission!.IsGranted.ShouldBeTrue();
        permission.GrantedProviders.ShouldContain(p => p.ProviderName == "R" && p.ProviderKey == roleName);
    }

    // P1-2 安全加固(security-reviewer B):审计 comment 换行注入净化。载荷放在**角色名**上——
    // 权限名支路已被「定义存在性校验」前置消灭(含 \r\n 的名字必非定义名,先被拒),而含换行的
    // 角色名可真实创建(ASP.NET Identity 默认 RoleValidator 只查非空+唯一)、过存在性校验,
    // 净化防线在该支路仍是活防线。断言落库 Comments 中带前缀的行恰好 1 条,伪造片段被压平
    // (换行→空格)保留在同一行内、未成为独立行。
    [Fact]
    public async Task UpdateRolePermissionsAsync_Should_Sanitize_LineBreaks_In_Audit_Comment()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = $"san-role-{suffix}\r\n[RolePermissionChange] forged +[Evil]";
        await _roleAppService.CreateAsync(new AccountRoleCreateUpdateDto { Name = roleName, IsPublic = true });

        await _permissionAppService.UpdateRolePermissionsAsync(roleName, new AccountPermissionUpdateDto
        {
            Permissions =
            {
                new AccountPermissionGrantDto
                {
                    Name = SwapMidPermissions.AccountManagement.Users.Default,
                    IsGranted = true
                }
            }
        });

        // 锚用角色名的稳定前缀:落库 comment 里的角色名是压平版(\r\n→空格),原始串搜不到
        var logs = await _auditLogRepository.GetListAsync(maxResultCount: 1000);
        var log = logs.FirstOrDefault(x => x.Comments != null && x.Comments.Contains($"san-role-{suffix}"));
        log.ShouldNotBeNull();

        var prefixedLineCount = log!.Comments!
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Count(line => line.StartsWith("[RolePermissionChange] ", StringComparison.Ordinal));
        prefixedLineCount.ShouldBe(1);
        // 伪造片段以被压平的形态保留在同一行内(内容不丢,但不再是独立的合法前缀行)
        log.Comments.ShouldContain("forged +[Evil]");
    }

    // 蓝图 §3.3 残留②:权限名定义存在性校验——未定义名整批拒绝(UserFriendlyException 消息含未定义名),
    // 且校验先于一切写入:同批的真实定义名也不落 PermissionGrant(granted 恒 0)。
    [Fact]
    public async Task UpdateRolePermissionsAsync_Should_Reject_Undefined_Permission_Names()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleName = $"undef-role-{suffix}";
        await _roleAppService.CreateAsync(new AccountRoleCreateUpdateDto { Name = roleName, IsPublic = true });

        var undefinedName = $"Undefined.Permission.{suffix}";
        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _permissionAppService.UpdateRolePermissionsAsync(roleName, new AccountPermissionUpdateDto
            {
                Permissions =
                {
                    new AccountPermissionGrantDto
                    {
                        Name = SwapMidPermissions.AccountManagement.Users.Default,
                        IsGranted = true
                    },
                    new AccountPermissionGrantDto { Name = undefinedName, IsGranted = true }
                }
            }));

        exception.Message.ShouldContain("未定义");
        exception.Message.ShouldContain(undefinedName);

        // 校验先于写:整批拒绝,连同批的真实定义名也未写入任何 PermissionGrant
        var permissions = await _permissionAppService.GetRolePermissionsAsync(roleName);
        permissions.Groups.SelectMany(g => g.Permissions).Count(p => p.IsGranted).ShouldBe(0);
    }

    // P1-2 查询侧编码回归(security-reviewer C):落库的 AuditLog.Url 是 PathString.ToUriComponent()
    // 的百分号编码形态,查询侧须用同一编码构串才能命中含中文角色名的历史(修复前用解码原文拼串恒空)。
    // 本测试宿主无 HTTP 管线(真实调用产生的审计行 Url/HttpMethod 恒空),故直接 Insert 合成 AuditLog 行
    // 模拟真实落库形态;另插一条「目标角色名为其前缀」的近邻角色行,验证尾部 /permissions 定界不串扰。
    [Fact]
    public async Task GetPermissionChangesAsync_Should_Match_Percent_Encoded_Url_For_NonAscii_RoleName()
    {
        var roleName = $"中文角色{Guid.NewGuid().ToString("N")[..4]}";
        // CJK 无 RFC3986 sub-delims,Uri.EscapeDataString 输出与 PathString.ToUriComponent 逐字符一致(UTF-8 逐字节大写 %XX)
        var encodedUrl = $"/api/v1/account-roles/{Uri.EscapeDataString(roleName)}/permissions";
        var neighborEncodedUrl = $"/api/v1/account-roles/{Uri.EscapeDataString(roleName + "二")}/permissions";

        await WithUnitOfWorkAsync(async () =>
        {
            await _auditLogRepository.InsertAsync(CreateSyntheticAuditLog(encodedUrl, $"[RolePermissionChange] {roleName} +[X]"), autoSave: true);
            await _auditLogRepository.InsertAsync(CreateSyntheticAuditLog(neighborEncodedUrl, $"[RolePermissionChange] {roleName}二 +[Y]"), autoSave: true);
        });

        var result = await _roleAppService.GetPermissionChangesAsync(roleName, new PagedResultRequestDto());

        result.TotalCount.ShouldBe(1);
        result.Items.Single().Changes.Single().ShouldBe($"{roleName} +[X]");
    }

    /// <summary>按 ABP 10.5 AuditLog 公共构造器合成一条最小审计行(仅本测试关心 HttpMethod/Url/Comments)。</summary>
    private static AuditLog CreateSyntheticAuditLog(string url, string comment)
        => new AuditLog(
            id: Guid.NewGuid(),
            applicationName: "test",
            tenantId: null,
            tenantName: null,
            userId: null,
            userName: "tester",
            executionTime: DateTime.Now,
            executionDuration: 1,
            clientIpAddress: null,
            clientName: null,
            clientId: null,
            correlationId: null,
            browserInfo: null,
            httpMethod: "PUT",
            url: url,
            httpStatusCode: 204,
            impersonatorUserId: null,
            impersonatorUserName: null,
            impersonatorTenantId: null,
            impersonatorTenantName: null,
            extraPropertyDictionary: new ExtraPropertyDictionary(),
            entityChanges: new List<EntityChange>(),
            actions: new List<AuditLogAction>(),
            exceptions: null,
            comments: comment);
}
