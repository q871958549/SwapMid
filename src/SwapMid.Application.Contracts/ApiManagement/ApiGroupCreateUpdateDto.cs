using System.ComponentModel.DataAnnotations;

namespace SwapMid.ApiManagement;

/// <summary>API 分组新增/编辑入参</summary>
public class ApiGroupCreateUpdateDto
{
    /// <summary>分组名称</summary>
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = null!;

    /// <summary>分组描述</summary>
    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>排序号(越小越靠前)</summary>
    public int Order { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}
