using System;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.ApiManagement;

/// <summary>API 路由新增/编辑入参</summary>
public class ApiRouteCreateUpdateDto
{
    /// <summary>所属分组 ID</summary>
    public Guid? GroupId { get; set; }

    /// <summary>路由名称</summary>
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = null!;

    /// <summary>路由描述</summary>
    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>路由路径</summary>
    [Required]
    [MaxLength(256)]
    public string Path { get; set; } = null!;

    /// <summary>HTTP 方法</summary>
    [Required]
    public HttpMethodType HttpMethod { get; set; }

    /// <summary>上游地址(转发目标)</summary>
    [Required]
    [MaxLength(512)]
    public string UpstreamUrl { get; set; } = null!;

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>排序号(越小越靠前)</summary>
    public int Order { get; set; }
}
