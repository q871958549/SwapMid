using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.ApiManagement;

/// <summary>API 分组（对 API 路由做业务归类，便于分组管理与展示）。</summary>
public class ApiGroup : FullAuditedAggregateRoot<Guid>
{
    /// <summary>分组名称。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>分组描述（可空）。</summary>
    public string? Description { get; private set; }

    /// <summary>排序序号（越小越靠前）。</summary>
    public int Order { get; private set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; private set; }

    private ApiGroup() { }

    public ApiGroup(Guid id, string name, string? description = null, int order = 0)
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), ApiGroupConsts.MaxNameLength);
        Description = description;
        Order = order;
        IsEnabled = true;
    }

    public void Update(string name, string? description, int order)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), ApiGroupConsts.MaxNameLength);
        Description = description;
        Order = order;
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
}
