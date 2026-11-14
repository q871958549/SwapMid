using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Identity;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.AuditLogging;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;

namespace SwapMid.AccountManagement;

[Authorize(SwapMidPermissions.AccountManagement.Roles.Default)]
public class AccountRoleAppService : ApplicationService, IAccountRoleAppService
{
    private const string DescriptionPropertyName = "Description";

    /// <summary>
    /// 转交 ABP <c>IIdentityRoleAppService</c> 的排序白名单（列 id 小写 → IdentityRole 实体属性名）。
    ///
    /// ABP 那侧最终走 <b>动态 LINQ</b> 解析这个串——裸传用户输入会让非法字段名抛异常返 500，
    /// 也是探测实体模型的口子（同 <c>AuditLogAppService</c> 已收窄的那条，本处是 A 批漏网的同类）。
    /// 当前前端角色页是客户端分页、不下推 Sorting，但接口本身对任何持凭据者开放，缺口是真的。
    ///
    /// 只有 <c>Name</c> 能进：描述存 ExtraProperties、成员数/权限数是平台侧派生列，
    /// 动态 LINQ 都排不了；前端对应列本就走客户端内存排序，不受影响。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SortWhitelist =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "Name",
        };
    // 角色级权限授权的 ProviderName 约定值，与 admin 锁定/审计判定共享同一常量(AccountPermissionSharedConsts)。
    private const string RoleProviderName = AccountPermissionSharedConsts.RoleProviderName;

    private readonly IIdentityRoleAppService _identityRoleAppService;
    private readonly IAccountRoleStatRepository _accountRoleStatRepository;
    private readonly IPermissionAppService _permissionAppService;
    private readonly IAuditLogRepository _auditLogRepository;

    public AccountRoleAppService(
        IIdentityRoleAppService identityRoleAppService,
        IAccountRoleStatRepository accountRoleStatRepository,
        IPermissionAppService permissionAppService,
        IAuditLogRepository auditLogRepository)
    {
        _identityRoleAppService = identityRoleAppService;
        _accountRoleStatRepository = accountRoleStatRepository;
        _permissionAppService = permissionAppService;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<PagedResultDto<AccountRoleDto>> GetListAsync(GetAccountRoleListInput input)
    {
        var filterInMemory = input.StartTime.HasValue || input.EndTime.HasValue;
        var roles = await _identityRoleAppService.GetListAsync(new GetIdentityRolesInput
        {
            Filter = input.Filter,
            // 未命中白名单一律回退 Name——与 ABP 自身 sorting 为空时的默认序一致，对现有调用方无可感知变更。
            Sorting = ListQuerySortingExtensions.ResolveWhitelistedSorting(
                input.Sorting, SortWhitelist, "Name", Logger),
            SkipCount = filterInMemory ? 0 : input.SkipCount,
            MaxResultCount = filterInMemory
                ? Math.Max(input.SkipCount + input.MaxResultCount, 1000)
                : input.MaxResultCount
        });

        var dtos = roles.Items.Select(ToDto).ToList();
        if (filterInMemory)
        {
            dtos = dtos
                .AsQueryable()
                .ApplyTimeRange(input.StartTime, input.EndTime, input.TimeType)
                .ToList();
        }

        var totalCount = filterInMemory ? dtos.Count : roles.TotalCount;
        var pagedItems = filterInMemory ? dtos.Skip(input.SkipCount).Take(input.MaxResultCount).ToList() : dtos;

        // 页内字典 join：两条聚合查询各取一次全量，避免逐角色查询（N+1）。
        await FillStatsAsync(pagedItems);

        return new PagedResultDto<AccountRoleDto>(totalCount, pagedItems);
    }

    public async Task<AccountRoleDto> GetAsync(Guid id)
    {
        var dto = ToDto(await _identityRoleAppService.GetAsync(id));
        // 单角色也走同一套全量聚合查询再取一条：角色量级几十~一二百，为单角色拉全量成本可忽略，
        // 换来仓储只需维护一套「批量」语义，不必另开单角色查询重载（择简，见蓝图 §3.2 P0-9）。
        await FillStatsAsync(new List<AccountRoleDto> { dto });
        return dto;
    }

    [Authorize(SwapMidPermissions.AccountManagement.Roles.Create)]
    public async Task<AccountRoleDto> CreateAsync(AccountRoleCreateUpdateDto input)
    {
        var createDto = new IdentityRoleCreateDto
        {
            Name = input.Name,
            IsDefault = input.IsDefault,
            IsPublic = input.IsPublic
        };
        createDto.SetProperty(DescriptionPropertyName, input.Description);

        var role = await _identityRoleAppService.CreateAsync(createDto);
        var dto = ToDto(role);
        await FillStatsAsync(new List<AccountRoleDto> { dto });
        return dto;
    }

    [Authorize(SwapMidPermissions.AccountManagement.Roles.Update)]
    public async Task<AccountRoleDto> UpdateAsync(Guid id, AccountRoleCreateUpdateDto input)
    {
        var updateDto = new IdentityRoleUpdateDto
        {
            Name = input.Name,
            IsDefault = input.IsDefault,
            IsPublic = input.IsPublic,
            ConcurrencyStamp = input.ConcurrencyStamp
        };
        updateDto.SetProperty(DescriptionPropertyName, input.Description);

        var role = await _identityRoleAppService.UpdateAsync(id, updateDto);
        var dto = ToDto(role);
        await FillStatsAsync(new List<AccountRoleDto> { dto });
        return dto;
    }

    [Authorize(SwapMidPermissions.AccountManagement.Roles.Delete)]
    public Task DeleteAsync(Guid id)
    {
        return _identityRoleAppService.DeleteAsync(id);
    }

    // 双 [Authorize] 为 AND 语义:复制=建角色(Create)+把源角色权限整份授予新角色(语义上等价改权限,须与
    // AccountPermissionAppService 的正门 Roles.Update 对齐),否则仅持 Create 者可借复制绕过授权限的门。
    [Authorize(SwapMidPermissions.AccountManagement.Roles.Create)]
    [Authorize(SwapMidPermissions.AccountManagement.Roles.Update)]
    public async Task<AccountRoleDto> CopyAsync(Guid sourceId, AccountRoleCopyDto input)
    {
        var source = await _identityRoleAppService.GetAsync(sourceId);

        // isDefault/isPublic 复制自源角色；IdentityRoleCreateDto 无 IsStatic 字段——新角色天然非内置角色。
        var createDto = new IdentityRoleCreateDto
        {
            Name = input.Name,
            IsDefault = source.IsDefault,
            IsPublic = source.IsPublic
        };
        var newRole = await _identityRoleAppService.CreateAsync(createDto);

        // 整份复制源角色当前已授予的权限（未授予的不传，UpdateAsync 只处理传入项，新角色默认全未授予）。
        var sourcePermissions = await _permissionAppService.GetAsync(RoleProviderName, source.Name);
        var grantedPermissions = sourcePermissions.Groups
            .SelectMany(g => g.Permissions)
            .Where(p => p.IsGranted)
            .Select(p => new UpdatePermissionDto { Name = p.Name, IsGranted = true })
            .ToArray();

        if (grantedPermissions.Length > 0)
        {
            await _permissionAppService.UpdateAsync(RoleProviderName, newRole.Name, new UpdatePermissionsDto
            {
                Permissions = grantedPermissions
            });
        }

        var dto = ToDto(newRole);
        // 不走 FillStatsAsync 重查库:UpdateAsync 刚写入的 PermissionGrant 与本方法同一 UoW,
        // SaveChanges 延后到 UoW 完成时才 flush,此刻重查询会读到 flush 前的旧值(读到 0)。
        // 两个值本就已知——成员数=刚建的新角色必为 0;权限数=刚授予的 grantedPermissions 条数。
        dto.MemberCount = 0;
        dto.PermissionCount = grantedPermissions.Length;
        return dto;
    }

    // P1-2 查询侧:从 AbpAuditLogs 按 httpMethod=PUT + url 子串匹配该角色的权限端点(前导 / 与尾部
    // /permissions 定界,角色名互为前后缀不串扰；表大,严禁全表拉回内存筛,过滤下推到仓储层,分页同样下推)。
    // 每条匹配的 AuditLog 对应一次 PUT 请求，映射为一条历史记录——即便该次请求未产生实际权限变化(diff 空)
    // 也照样返回一条 Changes 为空的记录，保持 TotalCount 与页内条数一致(若在此再按"是否有 diff"过滤会导致分页错位)。
    public async Task<PagedResultDto<RolePermissionChangeDto>> GetPermissionChangesAsync(string roleName, PagedResultRequestDto input)
    {
        // 落库的 AuditLog.Url 是 PathString.ToUriComponent() 的百分号编码形态(审计贡献者存 Request.Path.ToString())，
        // 查询侧必须用同一编码构串——直接拿路由解码原文拼串,含中文等非 ASCII 字符的角色名 Contains 永不命中(历史恒空)。
        var url = AuditUrlPath.ToUriComponent($"/api/v1/account-roles/{roleName}/permissions");

        var logs = await _auditLogRepository.GetListAsync(
            sorting: "ExecutionTime DESC",
            maxResultCount: input.MaxResultCount,
            skipCount: input.SkipCount,
            httpMethod: "PUT",
            url: url,
            includeDetails: false);

        var totalCount = await _auditLogRepository.GetCountAsync(httpMethod: "PUT", url: url);

        var items = logs.Select(log => new RolePermissionChangeDto
        {
            ExecutionTime = log.ExecutionTime,
            UserName = log.UserName,
            Changes = ExtractChangeLines(log.Comments)
        }).ToList();

        return new PagedResultDto<RolePermissionChangeDto>(totalCount, items);
    }

    /// <summary>从 AuditLog.Comments(多行,\n 分隔)里挑出带 <see cref="AccountPermissionSharedConsts.ChangeCommentPrefix"/>
    /// 前缀的行，并剥离该前缀(角色名+diff 部分原样保留,供前端展开明细)。</summary>
    private static List<string> ExtractChangeLines(string? comments)
    {
        if (string.IsNullOrEmpty(comments))
        {
            return new List<string>();
        }

        return comments
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith(AccountPermissionSharedConsts.ChangeCommentPrefix, StringComparison.Ordinal))
            .Select(line => line[AccountPermissionSharedConsts.ChangeCommentPrefix.Length..])
            .ToList();
    }

    private static AccountRoleDto ToDto(IdentityRoleDto role)
    {
        return new AccountRoleDto
        {
            Id = role.Id,
            Name = role.Name,
            IsDefault = role.IsDefault,
            IsStatic = role.IsStatic,
            IsPublic = role.IsPublic,
            CreationTime = role.CreationTime,
            Description = role.GetProperty<string>(DescriptionPropertyName),
            ConcurrencyStamp = role.ConcurrencyStamp
        };
    }

    /// <summary>页内字典 join 填充 MemberCount / PermissionCount，两条聚合查询各一次取全量。</summary>
    private async Task FillStatsAsync(List<AccountRoleDto> dtos)
    {
        if (dtos.Count == 0)
        {
            return;
        }

        var memberCounts = await _accountRoleStatRepository.GetMemberCountsAsync();
        var permissionCounts = await _accountRoleStatRepository.GetPermissionCountsAsync();

        foreach (var dto in dtos)
        {
            dto.MemberCount = memberCounts.GetValueOrDefault(dto.Id);
            dto.PermissionCount = permissionCounts.GetValueOrDefault(dto.Name);
        }
    }
}
