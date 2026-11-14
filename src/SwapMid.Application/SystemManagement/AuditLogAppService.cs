using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.AuditLogging;
using Volo.Abp.Domain.Entities;

namespace SwapMid.SystemManagement;

/// <summary>
/// 调用日志查看器。直接查询 ABP 审计日志存储（AbpAuditLogs），
/// 平台每次 HTTP API 调用（URL/方法/状态码/用户/耗时/异常/应用服务动作）都已被审计中间件自动记录。
/// </summary>
[Authorize(SwapMidPermissions.SystemManagement.AuditLogs.Default)]
public class AuditLogAppService : ApplicationService, IAuditLogAppService
{
    private readonly IAuditLogRepository _auditLogRepository;

    public AuditLogAppService(IAuditLogRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    public async Task<PagedResultDto<AuditLogDto>> GetListAsync(GetAuditLogListInput input)
    {
        // 排序白名单收窄：此前把 input.Sorting 裸传给 ABP 动态 LINQ 排序——前端传非法字段名会抛异常返 500，
        // 也给了探测实体模型的口子。改走白名单后未命中一律回退默认序（见 ResolveWhitelistedSorting）。
        var sorting = ListQuerySortingExtensions.ResolveWhitelistedSorting(
            input.Sorting, SortWhitelist, "ExecutionTime DESC", Logger);
        var httpStatusCode = input.HttpStatusCode.HasValue ? (HttpStatusCode?)input.HttpStatusCode.Value : null;

        var logs = await _auditLogRepository.GetListAsync(
            sorting: sorting,
            maxResultCount: input.MaxResultCount,
            skipCount: input.SkipCount,
            startTime: input.StartTime,
            endTime: input.EndTime,
            httpMethod: input.HttpMethod,
            url: input.Url,
            userName: input.UserName,
            hasException: input.HasException,
            httpStatusCode: httpStatusCode,
            includeDetails: false);

        var total = await _auditLogRepository.GetCountAsync(
            startTime: input.StartTime,
            endTime: input.EndTime,
            httpMethod: input.HttpMethod,
            url: input.Url,
            userName: input.UserName,
            hasException: input.HasException,
            httpStatusCode: httpStatusCode);

        return new PagedResultDto<AuditLogDto>(
            total,
            logs.Select(x => ToDto(x, includeDetails: false)).ToList());
    }

    // 服务端排序白名单（前端列 id 小写 → ABP AuditLog 实体属性名）：与 audit-logs/client.tsx 的可排序列一一对应。
    // 前端 hasException 是 DTO 派生列（实体侧只有 Exceptions 文本）、无法排序，该列已加 enableSorting: false。
    private static readonly IReadOnlyDictionary<string, string> SortWhitelist = new Dictionary<string, string>
    {
        ["executiontime"] = "ExecutionTime",
        ["httpmethod"] = "HttpMethod",
        ["url"] = "Url",
        ["httpstatuscode"] = "HttpStatusCode",
        ["username"] = "UserName",
        ["executionduration"] = "ExecutionDuration",
        ["clientipaddress"] = "ClientIpAddress",
    };

    public async Task<AuditLogDto> GetAsync(Guid id)
    {
        var log = await _auditLogRepository.FindAsync(id, includeDetails: true);
        if (log == null)
        {
            throw new EntityNotFoundException(typeof(AuditLog), id);
        }

        return ToDto(log, includeDetails: true);
    }

    private static AuditLogDto ToDto(AuditLog log, bool includeDetails)
    {
        var dto = new AuditLogDto
        {
            Id = log.Id,
            ApplicationName = log.ApplicationName,
            UserId = log.UserId,
            UserName = log.UserName,
            ExecutionTime = log.ExecutionTime,
            ExecutionDuration = log.ExecutionDuration,
            ClientIpAddress = log.ClientIpAddress,
            HttpMethod = log.HttpMethod,
            Url = log.Url,
            HttpStatusCode = log.HttpStatusCode,
            CorrelationId = log.CorrelationId,
            HasException = !string.IsNullOrEmpty(log.Exceptions),
        };

        if (includeDetails)
        {
            dto.BrowserInfo = log.BrowserInfo;
            dto.Exceptions = log.Exceptions;
            dto.Comments = log.Comments;
            dto.Actions = log.Actions?
                .OrderBy(a => a.ExecutionTime)
                .Select(a => new AuditLogActionDto
                {
                    ServiceName = a.ServiceName,
                    MethodName = a.MethodName,
                    Parameters = AuditLogParameterDisclosurePolicy.Protect(
                        a.ServiceName,
                        a.Parameters),
                    ExecutionTime = a.ExecutionTime,
                    ExecutionDuration = a.ExecutionDuration,
                })
                .ToList();
        }

        return dto;
    }
}
