using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.AccountManagement;

public interface ICurrentUserPermissionAppService : IApplicationService
{
    Task<CurrentUserPermissionDto> GetAsync();

    /// <summary>查询任意用户当前生效的权限清单(按分组组织,每项附授予来源，用于"他为什么有这权限"排查场景，
    /// 如账号管理页「查看有效权限」)。⚠️ 内层 ABP 权限体系对 provider "U" 的策略是
    /// <c>AbpIdentity.Users.ManagePermissions</c>(与角色权限页现状同级)，调用者除持有本方法要求的
    /// <c>AccountManagement.Users.Default</c> 外，还须持有该 ABP 内置权限，否则会被 ABP 拦截；
    /// 管理人群与角色权限页现状重合，可接受。</summary>
    Task<AccountUserPermissionDto> GetForUserAsync(Guid userId);
}
