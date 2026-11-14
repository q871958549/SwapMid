using System;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.EnterpriseManagement;

/// <summary>新建/更新部门入参</summary>
public class CompanyDepartmentCreateUpdateDto
{
    public Guid? EnterpriseId { get; set; }

    [MaxLength(EnterpriseConsts.MaxCodeLength)]
    public string? EnterpriseCode { get; set; }

    public Guid? ParentId { get; set; }

    [MaxLength(CompanyDepartmentConsts.MaxCodeLength)]
    public string? ParentCode { get; set; }

    [Required]
    [MaxLength(CompanyDepartmentConsts.MaxCodeLength)]
    public string Code { get; set; } = null!;

    [Required]
    [MaxLength(CompanyDepartmentConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    public int Level { get; set; } = 1;

    [MaxLength(CompanyDepartmentConsts.MaxFullPathLength)]
    public string? FullPath { get; set; }

    public Guid? LeaderEmployeeId { get; set; }

    [MaxLength(EmployeeConsts.MaxEmployeeNoLength)]
    public string? LeaderEmployeeNo { get; set; }

    public int Order { get; set; }

    public bool IsEnabled { get; set; } = true;
}
