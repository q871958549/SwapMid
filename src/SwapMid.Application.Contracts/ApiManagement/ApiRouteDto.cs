using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.ApiManagement;

/// <summary>API 路由</summary>
public class ApiRouteDto : FullAuditedEntityDto<Guid>
{
    /// <summary>所属分组 ID</summary>
    public Guid? GroupId { get; set; }
    /// <summary>所属分组名称</summary>
    public string? GroupName { get; set; }
    /// <summary>路由名称</summary>
    public string Name { get; set; } = null!;
    /// <summary>路由描述</summary>
    public string? Description { get; set; }
    /// <summary>路由路径</summary>
    public string Path { get; set; } = null!;
    /// <summary>HTTP 方法</summary>
    public HttpMethodType HttpMethod { get; set; }
    /// <summary>上游地址(转发目标)</summary>
    public string UpstreamUrl { get; set; } = null!;
    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }
    /// <summary>排序号(越小越靠前)</summary>
    public int Order { get; set; }
}
