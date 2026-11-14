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

    [HttpGet]
    public Task<PagedResultDto<AuditLogDto>> GetListAsync(GetAuditLogListInput input)
        => _service.GetListAsync(input);

    [HttpGet("{id}")]
    public Task<AuditLogDto> GetAsync(Guid id)
        => _service.GetAsync(id);
}
