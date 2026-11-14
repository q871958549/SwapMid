using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SwapMid.AccountManagement;

/// <summary>为已授权业务列表解析审计用户显示名。</summary>
public interface IAuditUserLookupAppService : IApplicationService
{
    /// <summary>
    /// 按用户 Id 批量解析显示名与登录名；只返回存在且当前租户可见的用户，不返回联系方式或员工资料。
    /// </summary>
    Task<ListResultDto<AuditUserLookupDto>> LookupAsync(
        AuditUserLookupInput input,
        CancellationToken cancellationToken = default);
}
