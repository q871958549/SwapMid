using System;
using System.Collections.Generic;
using Volo.Abp.Application.Dtos;

namespace SwapMid.SystemManagement;

/// <summary>API 调用日志（来自 ABP 审计日志 AbpAuditLogs，平台每次 HTTP 调用自动记录）。</summary>
public class AuditLogDto : EntityDto<Guid>
{
    /// <summary>应用名称。</summary>
    public string? ApplicationName { get; set; }

    /// <summary>调用用户 ID。</summary>
    public Guid? UserId { get; set; }

    /// <summary>调用用户名。</summary>
    public string? UserName { get; set; }

    /// <summary>请求时间。</summary>
    public DateTime ExecutionTime { get; set; }

    /// <summary>耗时（毫秒）。</summary>
    public int ExecutionDuration { get; set; }

    /// <summary>客户端 IP。</summary>
    public string? ClientIpAddress { get; set; }

    /// <summary>HTTP 方法（GET/POST/PUT/DELETE）。</summary>
    public string? HttpMethod { get; set; }

    /// <summary>请求 URL。</summary>
    public string? Url { get; set; }

    /// <summary>HTTP 状态码。</summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>浏览器信息。</summary>
    public string? BrowserInfo { get; set; }

    /// <summary>关联 ID（用于串联同一请求链路）。</summary>
    public string? CorrelationId { get; set; }

    /// <summary>是否发生异常（列表轻量标记；异常全文在详情）。</summary>
    public bool HasException { get; set; }

    /// <summary>异常全文（仅详情返回）。</summary>
    public string? Exceptions { get; set; }

    /// <summary>备注。</summary>
    public string? Comments { get; set; }

    /// <summary>应用服务动作明细（仅详情返回）。</summary>
    public List<AuditLogActionDto>? Actions { get; set; }
}

/// <summary>审计日志中的一次应用服务方法调用。</summary>
public class AuditLogActionDto
{
    /// <summary>应用服务名称。</summary>
    public string? ServiceName { get; set; }

    /// <summary>方法名称。</summary>
    public string? MethodName { get; set; }

    /// <summary>调用参数（JSON；数据源动作等敏感操作固定返回空对象）。</summary>
    public string? Parameters { get; set; }

    /// <summary>执行时间。</summary>
    public DateTime ExecutionTime { get; set; }

    /// <summary>耗时（毫秒）。</summary>
    public int ExecutionDuration { get; set; }
}
