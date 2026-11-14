using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.ApiManagement;

/// <summary>API 分组</summary>
public class ApiGroupDto : FullAuditedEntityDto<Guid>
{
    /// <summary>分组名称</summary>
    public string Name { get; set; } = null!;
    /// <summary>分组描述</summary>
    public string? Description { get; set; }
    /// <summary>排序号(越小越靠前)</summary>
    public int Order { get; set; }
    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }
}
