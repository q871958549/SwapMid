using System;
using SwapMid.SharedKernel.Employees;

namespace SwapMid.AccountManagement;

/// <summary>账号绑定员工的基本信息（账号页 Popover 展示用轻量只读 DTO，不含 HrProfile 敏感档案字段）。</summary>
public class BoundEmployeeInfoDto
{
    /// <summary>员工 ID。</summary>
    public Guid EmployeeId { get; set; }
    /// <summary>工号。</summary>
    public string EmployeeNo { get; set; } = null!;
    /// <summary>姓名。</summary>
    public string Name { get; set; } = null!;
    /// <summary>所属企业名称。</summary>
    public string? EnterpriseName { get; set; }
    /// <summary>所属部门名称。</summary>
    public string? DepartmentName { get; set; }
    /// <summary>岗位名称（关联岗位主数据）。</summary>
    public string? PositionName { get; set; }
    /// <summary>岗位名称文本快照（岗位主数据缺失时的兜底展示）。</summary>
    public string? Position { get; set; }
    /// <summary>邮箱。</summary>
    public string? Email { get; set; }
    /// <summary>手机号。</summary>
    public string? Phone { get; set; }
    /// <summary>在职状态（0=在职/1=停薪留职/2=离职）。</summary>
    public EmployeeStatus Status { get; set; }
}
