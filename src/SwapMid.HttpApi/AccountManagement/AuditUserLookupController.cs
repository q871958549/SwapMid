using System.Threading;
using System.Threading.Tasks;
using SwapMid.AccountManagement;
using SwapMid.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Auditing;
using Volo.Abp.Uow;

namespace SwapMid.Controllers.AccountManagement;

/// <summary>审计用户显示名查询接口。</summary>
[RemoteService]
[Area("app")]
[Route("api/v1/audit-users")]
[Authorize]
[DisableAuditing]
[UnitOfWork(false)]
public class AuditUserLookupController : SwapMidController, IAuditUserLookupAppService, IActionFilter
{
    private readonly IAuditUserLookupAppService _service;

    public AuditUserLookupController(IAuditUserLookupAppService service)
    {
        _service = service;
    }

    // 裸 Authorize 只验证 token/audience；第三方 product-query client 的 audience 相同，必须再要求平台 scope。
    void IActionFilter.OnActionExecuting(ActionExecutingContext context)
    {
        if (!User.HasPlatformScope())
        {
            context.Result = new ObjectResult("缺少访问本接口所需的授权范围") { StatusCode = 403 };
        }
    }

    void IActionFilter.OnActionExecuted(ActionExecutedContext context)
    {
    }

    /// <summary>按用户 Id 批量查询审计显示名与登录名。</summary>
    [HttpPost("lookup")]
    public Task<ListResultDto<AuditUserLookupDto>> LookupAsync(
        [FromBody] AuditUserLookupInput input,
        CancellationToken cancellationToken = default)
        => _service.LookupAsync(input, cancellationToken);
}
