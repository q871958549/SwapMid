using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SwapMid.SystemManagement;

/// <summary>调用日志查看器：查询 ABP 审计日志（每次 API 调用的 URL/状态/用户/耗时/异常/动作明细）。</summary>
public interface IAuditLogAppService : IApplicationService
{
    /// <summary>分页列表（不含异常全文与动作明细）。</summary>
    Task<PagedResultDto<AuditLogDto>> GetListAsync(GetAuditLogListInput input);

    /// <summary>单条详情（含异常全文、应用服务动作明细）。</summary>
    Task<AuditLogDto> GetAsync(Guid id);
}
