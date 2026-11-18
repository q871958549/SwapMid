using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SwapMid.Layout;
using Volo.Abp;
using Volo.Abp.Users;

namespace SwapMid.Controllers.Layout;

/// <summary>导航菜单使用度量上报端点。</summary>
[RemoteService]
[Area("app")]
[Authorize]
[Route("api/v1/nav-usage")]
public class NavUsageController : SwapMidController
{
    private readonly ILogger<NavUsageController> _logger;

    public NavUsageController(ILogger<NavUsageController> logger)
    {
        _logger = logger;
    }

    /// <summary>记录当前用户访问侧栏导航项的结构化度量日志。</summary>
    [HttpPost]
    public IActionResult Report([FromBody] NavUsageInputDto input)
    {
        _logger.LogInformation(
            "[NavUsage] userId={UserId} domain={Domain} group={Group} href={Href}",
            CurrentUser.Id,
            Sanitize(input.Domain),
            Sanitize(input.Group),
            Sanitize(input.Href));
        return NoContent();
    }

    private static string Sanitize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
}
