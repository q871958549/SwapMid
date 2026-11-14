using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.PermissionManagement;

namespace SwapMid.AccountManagement;

[Authorize]
public class CurrentUserPermissionAppService : ApplicationService, ICurrentUserPermissionAppService
{
    // 用户级权限授权的 ProviderName 约定值(ABP PermissionManagement 内置)。
    private const string UserProviderName = "U";

    private readonly IPermissionChecker _permissionChecker;
    private readonly IPermissionAppService _permissionAppService;

    public CurrentUserPermissionAppService(IPermissionChecker permissionChecker, IPermissionAppService permissionAppService)
    {
        _permissionChecker = permissionChecker;
        _permissionAppService = permissionAppService;
    }

    public async Task<CurrentUserPermissionDto> GetAsync()
    {
        var granted = new List<string>();
        foreach (var permission in GetPermissionNames(typeof(SwapMidPermissions)))
        {
            if (await _permissionChecker.IsGrantedAsync(permission))
            {
                granted.Add(permission);
            }
        }

        return new CurrentUserPermissionDto
        {
            GrantedPermissions = granted
        };
    }

    // P1-4:任意用户有效权限查询。IPermissionAppService.GetAsync("U", userId) 内部聚合全部 ManagementProviders
    // (User 直授 + Role,按 providerKey=userId 反查该用户实际所属角色)，返回的是该用户完整的"有效权限"，
    // 每项权限的 GrantedProviders 精确标注来源(R+角色名 / U+用户Id)——已反编译 ABP 10.5
    // RolePermissionManagementProvider.CheckAsync 确认：providerName=="U" 时会用 providerKey 解析的用户Id
    // 反查该用户的角色集合，而非当前登录者的角色，故对"查看他人权限"场景语义正确。
    // 类级 [Authorize] 为裸授权(仅要求已登录)，与本方法级 Users.Default 是 AND 语义(见 P0-11 同型注记)，
    // 合起来即"已登录 + 持有账号查看权限"才能调用。
    [Authorize(SwapMidPermissions.AccountManagement.Users.Default)]
    public async Task<AccountUserPermissionDto> GetForUserAsync(Guid userId)
    {
        var result = await _permissionAppService.GetAsync(UserProviderName, userId.ToString());
        return new AccountUserPermissionDto
        {
            UserId = userId,
            Groups = result.Groups.Select(group => new AccountPermissionGroupDto
            {
                Name = group.Name,
                DisplayName = group.DisplayName,
                Permissions = group.Permissions.Select(permission => new AccountPermissionDto
                {
                    Name = permission.Name,
                    DisplayName = permission.DisplayName,
                    ParentName = permission.ParentName,
                    IsGranted = permission.IsGranted,
                    GrantedProviders = permission.GrantedProviders.Select(provider => new AccountPermissionProviderDto
                    {
                        ProviderName = provider.ProviderName,
                        ProviderKey = provider.ProviderKey
                    }).ToList()
                }).ToList()
            }).ToList()
        };
    }

    private static IEnumerable<string> GetPermissionNames(Type type)
    {
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType == typeof(string) && field.GetValue(null) is string value &&
                value.StartsWith(SwapMidPermissions.GroupName + "."))
            {
                yield return value;
            }
        }

        foreach (var nestedType in type.GetNestedTypes(BindingFlags.Public))
        {
            foreach (var permission in GetPermissionNames(nestedType))
            {
                yield return permission;
            }
        }
    }
}
