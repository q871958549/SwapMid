using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.ApiManagement;

/// <summary>API 路由（网关将匹配到的入站请求转发到上游地址）。</summary>
public class ApiRoute : FullAuditedAggregateRoot<Guid>
{
    /// <summary>所属分组 Id（可空，关联 API 分组）。</summary>
    public Guid? GroupId { get; private set; }

    /// <summary>路由名称。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>路由描述（可空）。</summary>
    public string? Description { get; private set; }

    /// <summary>匹配路径（入站请求路径）。</summary>
    public string Path { get; private set; } = null!;

    /// <summary>匹配的 HTTP 方法（GET/POST/PUT/DELETE/PATCH/ALL）。</summary>
    public HttpMethodType HttpMethod { get; private set; }

    /// <summary>上游转发地址（匹配后请求转发到的目标 URL）。</summary>
    public string UpstreamUrl { get; private set; } = null!;

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>排序序号（越小越靠前）。</summary>
    public int Order { get; private set; }

    private ApiRoute() { }

    public ApiRoute(
        Guid id,
        string name,
        string path,
        HttpMethodType httpMethod,
        string upstreamUrl,
        Guid? groupId = null,
        string? description = null,
        int order = 0)
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), ApiRouteConsts.MaxNameLength);
        Path = Check.NotNullOrWhiteSpace(path, nameof(path), ApiRouteConsts.MaxPathLength);
        UpstreamUrl = Check.NotNullOrWhiteSpace(upstreamUrl, nameof(upstreamUrl), ApiRouteConsts.MaxUpstreamUrlLength);
        HttpMethod = httpMethod;
        GroupId = groupId;
        Description = description;
        Order = order;
        IsEnabled = true;
    }

    public void Update(
        string name,
        string path,
        HttpMethodType httpMethod,
        string upstreamUrl,
        Guid? groupId,
        string? description,
        int order)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), ApiRouteConsts.MaxNameLength);
        Path = Check.NotNullOrWhiteSpace(path, nameof(path), ApiRouteConsts.MaxPathLength);
        UpstreamUrl = Check.NotNullOrWhiteSpace(upstreamUrl, nameof(upstreamUrl), ApiRouteConsts.MaxUpstreamUrlLength);
        HttpMethod = httpMethod;
        GroupId = groupId;
        Description = description;
        Order = order;
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
}
