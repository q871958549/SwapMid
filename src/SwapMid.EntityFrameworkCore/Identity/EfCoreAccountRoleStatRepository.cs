using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Identity;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;

namespace SwapMid.EntityFrameworkCore.Identity;

/// <summary>
/// 角色成员数 / 权限数聚合统计实现。
/// 成员数复用 ABP 内置 <see cref="IIdentityRoleRepository.GetListWithUserCountAsync"/>
/// （已实测确认 ABP 10 自带，单条 SQL 完成 IdentityRole × IdentityUserRole 的 join + group by，
/// 且天然沿用 ABP 自身的软删 / 多租户过滤语义，优于自建同等 group-by）。
/// 权限数 ABP 无内置批量接口（<see cref="IPermissionGrantRepository"/> 只支持按单个
/// providerName+providerKey 查询，逐角色调用会 N+1），改为直接对 <see cref="PermissionGrant"/>
/// 分组查询——该实体已随 <c>SwapMidDbContext.OnModelCreating</c> 里的
/// <c>ConfigurePermissionManagement()</c> 进入本 DbContext 模型，无需新增 DbSet / 迁移（同
/// <c>EfCoreSchemaMetadataProvider</c> 用 <c>DbContext.Set&lt;T&gt;()</c> 访问未显式声明 DbSet 实体的用法）。
/// </summary>
public class EfCoreAccountRoleStatRepository : IAccountRoleStatRepository, ITransientDependency
{
    // 角色级权限授权的 ProviderName 约定值（对齐 AccountPermissionAppService 的 RoleProviderName）。
    private const string RoleProviderName = "R";

    private readonly IDbContextProvider<SwapMidDbContext> _dbContextProvider;
    private readonly IIdentityRoleRepository _identityRoleRepository;

    public EfCoreAccountRoleStatRepository(
        IDbContextProvider<SwapMidDbContext> dbContextProvider,
        IIdentityRoleRepository identityRoleRepository)
    {
        _dbContextProvider = dbContextProvider;
        _identityRoleRepository = identityRoleRepository;
    }

    public async Task<Dictionary<Guid, int>> GetMemberCountsAsync(CancellationToken cancellationToken = default)
    {
        // 不传 filter/skip/sorting，走默认 maxResultCount=int.MaxValue → 一次取全量角色的成员数。
        var rolesWithUserCount = await _identityRoleRepository.GetListWithUserCountAsync(
            cancellationToken: cancellationToken);

        return rolesWithUserCount.ToDictionary(x => x.Role.Id, x => (int)x.UserCount);
    }

    public async Task<Dictionary<string, int>> GetPermissionCountsAsync(CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        return await dbContext.Set<PermissionGrant>()
            .Where(x => x.ProviderName == RoleProviderName)
            .GroupBy(x => x.ProviderKey)
            .Select(g => new { RoleName = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleName, x => x.Count, cancellationToken);
    }
}
