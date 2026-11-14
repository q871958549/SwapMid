using System;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.EnterpriseManagement;

/// <summary>新建/更新岗位入参</summary>
public class PositionCreateUpdateDto
{
    public Guid? EnterpriseId { get; set; }

    [MaxLength(EnterpriseConsts.MaxCodeLength)]
    public string? EnterpriseCode { get; set; }

    public Guid? DepartmentId { get; set; }

    [MaxLength(CompanyDepartmentConsts.MaxCodeLength)]
    public string? DepartmentCode { get; set; }

    [Required]
    [MaxLength(PositionConsts.MaxCodeLength)]
    public string Code { get; set; } = null!;

    [Required]
    [MaxLength(PositionConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [MaxLength(PositionConsts.MaxLevelLength)]
    public string? Level { get; set; }

    [MaxLength(PositionConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public int Order { get; set; }

    public bool IsEnabled { get; set; } = true;
}
