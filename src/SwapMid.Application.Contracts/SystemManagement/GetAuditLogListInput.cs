using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.SystemManagement;

/// <summary>调用日志查询条件（默认按时间倒序）。</summary>
public class GetAuditLogListInput : PagedAndSortedResultRequestDto
{
    /// <summary>请求时间起（含）。</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>请求时间止（含）。</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>URL 模糊匹配。</summary>
    public string? Url { get; set; }

    /// <summary>用户名精确匹配。</summary>
    public string? UserName { get; set; }

    /// <summary>HTTP 方法（GET/POST/PUT/DELETE）。</summary>
    public string? HttpMethod { get; set; }

    /// <summary>仅看发生异常的调用。</summary>
    public bool? HasException { get; set; }

    /// <summary>HTTP 状态码精确匹配。</summary>
    public int? HttpStatusCode { get; set; }
}
