using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;

namespace SwapMid.AccountManagement;

[Authorize(SwapMidPermissions.AccountManagement.Roles.Update)]
public class AccountPermissionAppService : ApplicationService, IAccountPermissionAppService
{
    private readonly IPermissionAppService _permissionAppService;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly ILookupNormalizer _lookupNormalizer;
    private readonly IAuditingManager _auditingManager;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;

    public AccountPermissionAppService(
        IPermissionAppService permissionAppService,
        IIdentityRoleRepository roleRepository,
        ILookupNormalizer lookupNormalizer,
        IAuditingManager auditingManager,
        IPermissionDefinitionManager permissionDefinitionManager)
    {
        _permissionAppService = permissionAppService;
        _roleRepository = roleRepository;
        _lookupNormalizer = lookupNormalizer;
        _auditingManager = auditingManager;
        _permissionDefinitionManager = permissionDefinitionManager;
    }

    public async Task<AccountRolePermissionDto> GetRolePermissionsAsync(string roleName)
    {
        var result = await _permissionAppService.GetAsync(AccountPermissionSharedConsts.RoleProviderName, roleName);
        return new AccountRolePermissionDto
        {
            RoleName = roleName,
            Groups = result.Groups.Select(group => new AccountPermissionGroupDto
            {
                Name = group.Name,
                DisplayName = group.DisplayName,
                Permissions = group.Permissions.Select(permission => new AccountPermissionDto
                {
                    Name = permission.Name,
                    DisplayName = permission.DisplayName,
                    ParentName = permission.ParentName,
                    IsGranted = permission.IsGranted
                }).ToList()
            }).ToList()
        };
    }

    public async Task UpdateRolePermissionsAsync(string roleName, AccountPermissionUpdateDto input)
    {
        // 防自锁:admin 恒有全部权限(内置种子机制保证,新增权限随 DbMigrator 自动授予),
        // 在此改权限会造成全系统自锁(种子恢复需重跑 DbMigrator,非常规操作)。
        // 与 AdminLockedPermissionAppService(P1-5,覆写 ABP 原生 IPermissionAppService 拦其余通道)
        // 共享同一判定与文案(AccountPermissionSharedConsts)，防止两处漂移。
        if (AccountPermissionSharedConsts.IsAdminRole(roleName))
        {
            throw new UserFriendlyException(AccountPermissionSharedConsts.AdminRoleLockedMessage);
        }

        // 存在性校验:堵孤儿 PermissionGrant——不存在的角色名此前仍可写出 PermissionGrant 脏数据
        // (无权限影响但污染表，见 P0-8 复查发现的存量小坑)。
        var role = await _roleRepository.FindByNormalizedNameAsync(
            _lookupNormalizer.NormalizeName(roleName), includeDetails: false);
        if (role == null)
        {
            throw new UserFriendlyException("角色不存在");
        }

        // 权限名定义存在性校验(蓝图 §3.3 残留②):未定义的名字若放行,内层 PermissionManager.SetAsync
        // 对 granted=true 的会写出孤儿 PermissionGrant(P1 端到端验证实证:PUT 假名 204 落库)。
        // 一次取全量定义成 HashSet(约 171 个,比逐名 GetOrNullAsync 稳),整批校验、失败即拒——
        // 放在 diff 取旧状态之前,校验失败不浪费 GetAsync。
        var definedNames = (await _permissionDefinitionManager.GetPermissionsAsync())
            .Select(p => p.Name)
            .ToHashSet();
        var undefinedNames = input.Permissions
            .Select(p => p.Name)
            .Where(name => !definedNames.Contains(name))
            .Distinct()
            .ToList();
        if (undefinedNames.Count > 0)
        {
            throw new UserFriendlyException($"以下权限名未定义: {string.Join(",", undefinedNames)}");
        }

        // P1-2 权限变更语义化审计:写前取旧状态算 granted 集合，写后与本次目标状态 diff，
        // 追加到本次 PUT 请求的 AuditLog.Comments(固定前缀便于按 URL+前缀检索，
        // 见 AccountRoleAppService.GetPermissionChangesAsync)。无变更(diff 空)不写。
        var before = await _permissionAppService.GetAsync(AccountPermissionSharedConsts.RoleProviderName, roleName);
        var beforeGrantedNames = before.Groups
            .SelectMany(g => g.Permissions)
            .Where(p => p.IsGranted)
            .Select(p => p.Name)
            .ToHashSet();

        await _permissionAppService.UpdateAsync(AccountPermissionSharedConsts.RoleProviderName, roleName, new UpdatePermissionsDto
        {
            Permissions = input.Permissions.Select(permission => new UpdatePermissionDto
            {
                Name = permission.Name,
                IsGranted = permission.IsGranted
            }).ToArray()
        });

        AppendChangeAuditComment(roleName, input, beforeGrantedNames);
    }

    /// <summary>把本次 PUT 请求相对旧状态的 granted diff 追加到当前请求的 AuditLog.Comments；diff 为空不写。
    /// 整条 comment 保证单行：roleName 与权限名均来自请求可控输入，含 \r\n 时若原样拼入，
    /// 会伪造出第二条带合法前缀的 diff 行(查询侧 <see cref="AccountRoleAppService"/> 按行提取)，写入前一律压平。</summary>
    private void AppendChangeAuditComment(string roleName, AccountPermissionUpdateDto input, HashSet<string> beforeGrantedNames)
    {
        var added = input.Permissions
            .Where(p => p.IsGranted && !beforeGrantedNames.Contains(p.Name))
            .Select(p => StripLineBreaks(p.Name))
            .ToList();
        var removed = input.Permissions
            .Where(p => !p.IsGranted && beforeGrantedNames.Contains(p.Name))
            .Select(p => StripLineBreaks(p.Name))
            .ToList();

        if (added.Count == 0 && removed.Count == 0)
        {
            return;
        }

        var comment = new StringBuilder(AccountPermissionSharedConsts.ChangeCommentPrefix).Append(StripLineBreaks(roleName));
        if (added.Count > 0)
        {
            comment.Append(" +[").Append(string.Join(",", added)).Append(']');
        }
        if (removed.Count > 0)
        {
            comment.Append(" -[").Append(string.Join(",", removed)).Append(']');
        }

        _auditingManager.Current?.Log.Comments.Add(comment.ToString());
    }

    /// <summary>换行注入净化：\r/\n 替换为空格(string.Replace(char,char) 无命中时返回原实例,零分配)。</summary>
    private static string StripLineBreaks(string value)
        => value.Replace('\r', ' ').Replace('\n', ' ');
}
