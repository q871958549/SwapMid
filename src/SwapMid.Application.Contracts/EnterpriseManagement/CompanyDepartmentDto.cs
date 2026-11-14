using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.EnterpriseManagement;

/// <summary>部门信息</summary>
public class CompanyDepartmentDto : FullAuditedEntityDto<Guid>
{
    public Guid EnterpriseId { get; set; }
    public string? EnterpriseCode { get; set; }
    public string? EnterpriseName { get; set; }
    public EntityReferenceDto? Enterprise { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentCode { get; set; }
    public string? ParentName { get; set; }
    public EntityReferenceDto? Parent { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int Level { get; set; }
    public string? FullPath { get; set; }
    public Guid? LeaderEmployeeId { get; set; }
    public string? LeaderEmployeeNo { get; set; }
    public string? LeaderEmployeeName { get; set; }
    public EntityReferenceDto? LeaderEmployee { get; set; }
    public int Order { get; set; }
    public bool IsEnabled { get; set; }
}
