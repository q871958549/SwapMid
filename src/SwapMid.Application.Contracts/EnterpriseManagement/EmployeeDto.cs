using System;
using SwapMid.SharedKernel.Employees;
using Volo.Abp.Application.Dtos;

namespace SwapMid.EnterpriseManagement;

/// <summary>员工信息</summary>
public class EmployeeDto : FullAuditedEntityDto<Guid>
{
    /// <summary>所属企业 ID</summary>
    public Guid EnterpriseId { get; set; }
    /// <summary>所属企业编码</summary>
    public string? EnterpriseCode { get; set; }
    /// <summary>所属企业名称</summary>
    public string? EnterpriseName { get; set; }
    /// <summary>所属企业对象</summary>
    public EntityReferenceDto? Enterprise { get; set; }
    /// <summary>部门 ID</summary>
    public Guid? DepartmentId { get; set; }
    /// <summary>部门编码</summary>
    public string? DepartmentCode { get; set; }
    /// <summary>部门名称</summary>
    public string? DepartmentName { get; set; }
    /// <summary>所属部门对象</summary>
    public EntityReferenceDto? Department { get; set; }
    /// <summary>岗位 ID</summary>
    public Guid? PositionId { get; set; }
    /// <summary>岗位编码</summary>
    public string? PositionCode { get; set; }
    /// <summary>岗位名称</summary>
    public string? PositionName { get; set; }
    /// <summary>岗位对象</summary>
    public EntityReferenceDto? PositionRef { get; set; }
    /// <summary>关联平台账号 ID</summary>
    public Guid? UserId { get; set; }
    /// <summary>工号</summary>
    public string EmployeeNo { get; set; } = null!;
    /// <summary>姓名</summary>
    public string Name { get; set; } = null!;
    /// <summary>邮箱</summary>
    public string? Email { get; set; }
    /// <summary>手机号</summary>
    public string? Phone { get; set; }
    /// <summary>岗位名称（文本）</summary>
    public string? Position { get; set; }
    /// <summary>在职状态（0=在职/1=停薪留职/2=离职）</summary>
    public EmployeeStatus Status { get; set; }
}
