using System;
using System.Threading.Tasks;
using SwapMid.SystemManagement;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace SwapMid.Controllers.SystemManagement;

[RemoteService]
[Area("app")]
[Route("api/v1/audit-logs")]
public class AuditLogController : SwapMidController, IAuditLogAppService
{
    private readonly IAuditLogAppService _service;

    public AuditLogController(IAuditLogAppService service)
    {
        _service = service;
    }

    /// <summary>分页检索系统 API 请求与安全审计日志。</summary>
    [HttpGet]
    public Task<PagedResultDto<AuditLogDto>> GetListAsync(GetAuditLogListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单条审计日志的完整动作及参数变更明细。</summary>
    [HttpGet("{id}")]
    public Task<AuditLogDto> GetAsync(Guid id)
        => _service.GetAsync(id);
}
