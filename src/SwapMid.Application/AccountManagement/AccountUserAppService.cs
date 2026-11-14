using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.SharedKernel;
using SwapMid.SharedKernel.Employees;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp;

namespace SwapMid.AccountManagement;

[Authorize(SwapMidPermissions.AccountManagement.Users.Default)]
public class AccountUserAppService : ApplicationService, IAccountUserAppService
{
    private readonly IIdentityUserAppService _identityUserAppService;
    private readonly IdentityUserManager _userManager;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly IEmployeeDirectory _employeeDirectory;
    private readonly IEmployeeUserBinder _employeeUserBinder;
    private readonly IOrganizationDirectory _organizationDirectory;
    private readonly EmployeeAccountProvisioner _accountProvisioner;
    // 账号→员工绑定信息投影（单条/批量填充 + 详情查询）拆到独立类，见该类型注释（架构测试 FileLengthGuardTests）。
    private readonly AccountUserEmployeeBindingResolver _bindingResolver;
    private readonly AccountUserRoleNamesResolver _roleNamesResolver;

    public AccountUserAppService(
        IIdentityUserAppService identityUserAppService,
        IdentityUserManager userManager,
        IRepository<IdentityUser, Guid> userRepository,
        IEmployeeDirectory employeeDirectory,
        IEmployeeUserBinder employeeUserBinder,
        IOrganizationDirectory organizationDirectory,
        EmployeeAccountProvisioner accountProvisioner,
        AccountUserEmployeeBindingResolver bindingResolver,
        AccountUserRoleNamesResolver roleNamesResolver)
    {
        _identityUserAppService = identityUserAppService;
        _userManager = userManager;
        _userRepository = userRepository;
        _employeeDirectory = employeeDirectory;
        _employeeUserBinder = employeeUserBinder;
        _organizationDirectory = organizationDirectory;
        _accountProvisioner = accountProvisioner;
        _bindingResolver = bindingResolver;
        _roleNamesResolver = roleNamesResolver;
    }

    // 真·服务端分页：直接在 IdentityUser queryable 上把搜索/部门/状态/时间范围全部下推到 SQL，
    // 再 Count + Skip/Take 只取当页。彻底取代旧的「先拉 1000 条再内存过滤分页」权宜（破千账号会被截断）。
    public async Task<PagedResultDto<AccountUserDto>> GetListAsync(GetAccountUserListInput input)
    {
        input.MaxResultCount = AccountManagementConsts.Users.ClampListPageSize(input.MaxResultCount);

        var query = await _userRepository.GetQueryableAsync();

        // 部门筛选：账号→员工是反向关联，先取部门(可含下级)下绑定了账号的员工 UserId 集合，再筛账号。
        if (input.DepartmentId.HasValue)
        {
            var departmentIds = input.IncludeChildDepartments
                ? await _organizationDirectory.GetSelfAndDescendantDepartmentIdsAsync(input.DepartmentId.Value)
                : new List<Guid> { input.DepartmentId.Value };

            var boundUserIds = await _employeeDirectory
                .GetBoundUserIdsByDepartmentIdsAsync(departmentIds);

            query = query.Where(u => boundUserIds.Contains(u.Id));
        }

        // 角色筛选：直接在 IdentityUser.Roles 导航集合上下推(EF 可译为 join/exists),与其余筛选正交组合；
        // 与部门筛选同守本域 CLAUDE.md 坑①——禁止先拉全量再内存 Where。
        if (input.RoleId.HasValue)
        {
            query = query.Where(u => u.Roles.Any(r => r.RoleId == input.RoleId.Value));
        }

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filter = input.Filter.Trim();
            List<Guid>? pinyinIds = null;
            if (EmployeeNamePinyin.LooksLikeInitialsQuery(filter))
            {
                // 组织规模投影姓名即可；纯字母查询 SQL Contains 打不中「田烨」。
                var names = await AsyncExecuter.ToListAsync(query.Select(u => new { u.Id, u.Name }));
                pinyinIds = EmployeeNamePinyin.MatchIds(names.Select(item => (item.Id, (string?)item.Name)), filter);
            }

            query = pinyinIds is { Count: > 0 }
                ? query.Where(u =>
                    u.UserName.Contains(filter) ||
                    (u.Name != null && u.Name.Contains(filter)) ||
                    (u.Email != null && u.Email.Contains(filter)) ||
                    (u.PhoneNumber != null && u.PhoneNumber.Contains(filter)) ||
                    pinyinIds.Contains(u.Id))
                : query.Where(u =>
                    u.UserName.Contains(filter) ||
                    (u.Name != null && u.Name.Contains(filter)) ||
                    (u.Email != null && u.Email.Contains(filter)) ||
                    (u.PhoneNumber != null && u.PhoneNumber.Contains(filter)));
        }

        if (input.IsActive.HasValue)
        {
            query = query.Where(u => u.IsActive == input.IsActive.Value);
        }

        query = query.ApplyTimeRange(input.StartTime, input.EndTime, input.TimeType);

        var totalCount = await AsyncExecuter.CountAsync(query);

        var users = await AsyncExecuter.ToListAsync(
            query
                .ApplyWhitelistedSorting(input.Sorting, SortWhitelist, "username")
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));

        var dtos = await MapToDtosAsync(users);
        return new PagedResultDto<AccountUserDto>(totalCount, dtos);
    }

    // 服务端排序白名单（IdentityUser 直属字段，列 id 小写 → 排序委托）：未命中回退账号名，杜绝排序注入。
    // 关联列（所属角色）需 join，不在白名单（前端已禁排）。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<IdentityUser>, bool, IOrderedQueryable<IdentityUser>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<IdentityUser>, bool, IOrderedQueryable<IdentityUser>>>
        {
            ["username"] = (q, d) => d ? q.OrderByDescending(u => u.UserName) : q.OrderBy(u => u.UserName),
            ["name"] = (q, d) => d ? q.OrderByDescending(u => u.Name) : q.OrderBy(u => u.Name),
            ["email"] = (q, d) => d ? q.OrderByDescending(u => u.Email) : q.OrderBy(u => u.Email),
            ["phonenumber"] = (q, d) => d ? q.OrderByDescending(u => u.PhoneNumber) : q.OrderBy(u => u.PhoneNumber),
            ["isactive"] = (q, d) => d ? q.OrderByDescending(u => u.IsActive) : q.OrderBy(u => u.IsActive),
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(u => u.CreationTime) : q.OrderBy(u => u.CreationTime),
        };

    public async Task<AccountUserDto> GetAsync(Guid id)
    {
        var user = await _identityUserAppService.GetAsync(id);
        return await ToDtoAsync(user);
    }

    [Authorize(SwapMidPermissions.AccountManagement.Users.Create)]
    public async Task<AccountUserDto> CreateAsync(AccountUserCreateDto input)
    {
        var user = await _identityUserAppService.CreateAsync(new IdentityUserCreateDto
        {
            UserName = input.UserName,
            Name = input.Name,
            Surname = input.Surname,
            Email = input.Email,
            PhoneNumber = input.PhoneNumber,
            Password = input.Password,
            IsActive = input.IsActive,
            RoleNames = input.RoleNames.ToArray()
        });

        await BindEmployeeAsync(user.Id, input.EmployeeId);
        return await ToDtoAsync(user);
    }

    [Authorize(SwapMidPermissions.AccountManagement.Users.Update)]
    public async Task<AccountUserDto> UpdateAsync(Guid id, AccountUserUpdateDto input)
    {
        var user = await _identityUserAppService.UpdateAsync(id, new IdentityUserUpdateDto
        {
            UserName = input.UserName,
            Name = input.Name,
            Surname = input.Surname,
            Email = input.Email,
            PhoneNumber = input.PhoneNumber,
            IsActive = input.IsActive,
            RoleNames = input.RoleNames.ToArray()
        });

        await BindEmployeeAsync(user.Id, input.EmployeeId);
        return await ToDtoAsync(user);
    }

    /// <summary>启用或停用账号（列表行内切换，不走完整编辑）。</summary>
    [Authorize(SwapMidPermissions.AccountManagement.Users.Update)]
    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var user = await _userManager.GetByIdAsync(id);
        user.SetIsActive(isActive);
        (await _userManager.UpdateAsync(user)).CheckErrors();
    }

    // 批量分配角色：追加 = 现有角色 ∪ 指定角色；覆盖 = 直接替换为指定集合。
    // 逐用户处理并吞掉单个失败（记入 Messages），避免一个用户出错让整批回滚。
    [Authorize(SwapMidPermissions.AccountManagement.Users.Update)]
    public async Task<BatchOperationResultDto> BatchSetRolesAsync(BatchSetRolesInput input)
    {
        var userIds = input.UserIds.Distinct().ToList();
        var roleNames = input.RoleNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new BatchOperationResultDto { Requested = userIds.Count };
        foreach (var userId in userIds)
        {
            try
            {
                var target = roleNames;
                if (input.Append)
                {
                    var current = await _identityUserAppService.GetRolesAsync(userId);
                    target = current.Items.Select(r => r.Name)
                        .Union(roleNames, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                await _identityUserAppService.UpdateRolesAsync(userId, new IdentityUserUpdateRolesDto
                {
                    RoleNames = target.ToArray(),
                });
                result.Succeeded++;
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Messages.Add($"{userId}: {ex.Message}");
                Logger.LogWarning(ex, "批量分配角色失败（UserId={UserId}）", userId);
            }
        }

        return result;
    }

    // 员工忘记密码 → 管理员一键重置为初始密码，并按系统开关决定是否强制其下次登录改密（与建号同口径）
    [Authorize(SwapMidPermissions.AccountManagement.Users.Update)]
    public async Task ResetPasswordAsync(Guid id)
    {
        var user = await _userManager.GetByIdAsync(id);
        var defaultPassword = await _accountProvisioner.GetDefaultPasswordAsync();

        if (await _userManager.HasPasswordAsync(user))
        {
            (await _userManager.RemovePasswordAsync(user)).CheckErrors();
        }
        (await _userManager.AddPasswordAsync(user, defaultPassword)).CheckErrors();

        user.SetShouldChangePasswordOnNextLogin(await _accountProvisioner.ShouldForceChangePasswordAsync());
        (await _userManager.UpdateAsync(user)).CheckErrors();
    }

    // 角色页「分配账号」：以勾选集合为该角色的完整成员名单做全量同步（缺的加、多的移），
    // 逐用户容错（与批量分配角色同口径）。
    [Authorize(SwapMidPermissions.AccountManagement.Users.Update)]
    public async Task<BatchOperationResultDto> SetRoleMembersAsync(SetRoleMembersInput input)
    {
        var roleName = input.RoleName.Trim();
        var desired = input.UserIds.Distinct().ToHashSet();
        var current = (await _userManager.GetUsersInRoleAsync(roleName)).Select(x => x.Id).ToHashSet();

        var changes = desired.Except(current).Select(id => (Id: id, Add: true))
            .Concat(current.Except(desired).Select(id => (Id: id, Add: false)))
            .ToList();

        var result = new BatchOperationResultDto { Requested = changes.Count };
        foreach (var (id, add) in changes)
        {
            try
            {
                var user = await _userManager.GetByIdAsync(id);
                var op = add
                    ? await _userManager.AddToRoleAsync(user, roleName)
                    : await _userManager.RemoveFromRoleAsync(user, roleName);
                op.CheckErrors();
                result.Succeeded++;
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Messages.Add($"{id}: {ex.Message}");
                Logger.LogWarning(ex, "同步角色成员失败（Role={Role}, UserId={UserId}, Add={Add}）", roleName, id, add);
            }
        }

        return result;
    }

    [Authorize(SwapMidPermissions.AccountManagement.Users.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        await _employeeUserBinder.UnbindAllByUserIdAsync(id);
        await _identityUserAppService.DeleteAsync(id);
    }

    [Authorize(SwapMidPermissions.AccountManagement.Users.Create)]
    public async Task<EmployeeAccountProvisionResultDto> GenerateFromEmployeeAsync(GenerateEmployeeAccountInput input)
    {
        var result = await _accountProvisioner.ProvisionAsync(input.EmployeeId);
        return ToDto(result);
    }

    public async Task<ListResultDto<AccountRoleDto>> GetAssignableRolesAsync()
    {
        var roles = await _identityUserAppService.GetAssignableRolesAsync();
        return new ListResultDto<AccountRoleDto>(roles.Items.Select(ToDto).ToList());
    }

    // 角色分配弹窗：全量账号 + 一次批量角色映射（查询次数不随账号行数增长）。
    public async Task<ListResultDto<AccountUserLiteDto>> GetAllForAssignmentAsync()
    {
        var userQuery = await _userRepository.GetQueryableAsync();
        var users = await AsyncExecuter.ToListAsync(userQuery.OrderBy(u => u.UserName));
        var roleNamesByUserId = await _roleNamesResolver.GetRoleNamesByUserIdsAsync(users.Select(u => u.Id));

        var dtos = users.Select(u => new AccountUserLiteDto
        {
            Id = u.Id,
            UserName = u.UserName,
            Name = u.Name,
            RoleNames = roleNamesByUserId.GetValueOrDefault(u.Id) ?? new List<string>()
        }).ToList();

        return new ListResultDto<AccountUserLiteDto>(dtos);
    }

    // 账号页「点击绑定员工标签」Popover 专用——权限挂 Users.Default（类级已含，此处显式标注语义）：
    // 账号管理员≠员工主数据管理员，权限矩阵解耦，不要求调用者持有 EnterpriseManagement.Employees.Default。
    [Authorize(SwapMidPermissions.AccountManagement.Users.Default)]
    public Task<BoundEmployeeInfoDto?> GetBoundEmployeeAsync(Guid id)
        => _bindingResolver.GetBoundEmployeeInfoAsync(id);

    private async Task<AccountUserDto> ToDtoAsync(IdentityUserDto user)
    {
        var roleNames = await _roleNamesResolver.GetRoleNamesByUserIdsAsync(new[] { user.Id });
        var dto = new AccountUserDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Name = user.Name,
            Surname = user.Surname,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            IsActive = user.IsActive,
            RoleNames = roleNames.GetValueOrDefault(user.Id) ?? new List<string>(),
            CreationTime = user.CreationTime,
            CreatorId = user.CreatorId,
            LastModificationTime = user.LastModificationTime,
            LastModifierId = user.LastModifierId
        };

        await _bindingResolver.FillBindingAsync(dto);
        return dto;
    }

    // 当页批量映射：角色名一次 join，员工/企业/部门绑定一次批量查（查询次数不随当页行数增长）。
    private async Task<List<AccountUserDto>> MapToDtosAsync(List<IdentityUser> users)
    {
        var roleNamesByUserId = await _roleNamesResolver.GetRoleNamesByUserIdsAsync(users.Select(u => u.Id));
        var dtos = users.Select(user => new AccountUserDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Name = user.Name,
            Surname = user.Surname,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            IsActive = user.IsActive,
            RoleNames = roleNamesByUserId.GetValueOrDefault(user.Id) ?? new List<string>(),
            CreationTime = user.CreationTime,
            CreatorId = user.CreatorId,
            LastModificationTime = user.LastModificationTime,
            LastModifierId = user.LastModifierId
        }).ToList();

        await _bindingResolver.FillBindingsAsync(dtos);
        return dtos;
    }

    private static AccountRoleDto ToDto(IdentityRoleDto role)
    {
        return new AccountRoleDto
        {
            Id = role.Id,
            Name = role.Name,
            IsDefault = role.IsDefault,
            IsStatic = role.IsStatic,
            IsPublic = role.IsPublic
        };
    }

    private async Task BindEmployeeAsync(Guid userId, Guid? employeeId)
    {
        await _employeeUserBinder.UnbindAllByUserIdAsync(userId);

        if (!employeeId.HasValue)
        {
            return;
        }

        var employee = await _employeeDirectory.FindAsync(employeeId.Value)
            ?? throw new UserFriendlyException("所选员工不存在");
        if (employee.UserId.HasValue && employee.UserId.Value != userId)
        {
            throw new UserFriendlyException("该员工已绑定其他账号");
        }

        await _employeeUserBinder.BindAsync(employee.Id, userId);
    }

    private static EmployeeAccountProvisionResultDto ToDto(EmployeeAccountProvisionResult result)
        => new()
        {
            EmployeeId = result.EmployeeId,
            UserId = result.UserId,
            UserName = result.UserName,
            Created = result.Created,
            AlreadyBound = result.AlreadyBound,
            Message = result.Message,
        };
}
