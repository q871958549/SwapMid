using System;
using System.ComponentModel.DataAnnotations;
using SwapMid.SharedKernel.Employees;

namespace SwapMid.EnterpriseManagement;

/// <summary>新建/更新员工入参</summary>
public class EmployeeCreateUpdateDto
{
    /// <summary>所属企业 ID</summary>
    public Guid? EnterpriseId { get; set; }

    /// <summary>所属企业编码</summary>
    [MaxLength(EnterpriseConsts.MaxCodeLength)]
    public string? EnterpriseCode { get; set; }

    /// <summary>部门 ID</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>部门编码</summary>
    [MaxLength(CompanyDepartmentConsts.MaxCodeLength)]
    public string? DepartmentCode { get; set; }

    /// <summary>岗位 ID</summary>
    public Guid? PositionId { get; set; }

    /// <summary>岗位编码</summary>
    [MaxLength(PositionConsts.MaxCodeLength)]
    public string? PositionCode { get; set; }

    /// <summary>关联平台账号 ID</summary>
    public Guid? UserId { get; set; }

    /// <summary>工号</summary>
    [Required]
    [MaxLength(EmployeeConsts.MaxEmployeeNoLength)]
    public string EmployeeNo { get; set; } = null!;

    /// <summary>姓名</summary>
    [Required]
    [MaxLength(EmployeeConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>邮箱</summary>
    [MaxLength(EmployeeConsts.MaxEmailLength)]
    public string? Email { get; set; }

    /// <summary>手机号</summary>
    [MaxLength(EmployeeConsts.MaxPhoneLength)]
    public string? Phone { get; set; }

    /// <summary>岗位名称（文本）</summary>
    [MaxLength(EmployeeConsts.MaxPositionLength)]
    public string? Position { get; set; }

    /// <summary>在职状态（0=在职/1=停薪留职/2=离职）</summary>
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
}
