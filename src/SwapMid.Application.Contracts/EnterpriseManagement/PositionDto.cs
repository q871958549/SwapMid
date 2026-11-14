using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.EnterpriseManagement;

/// <summary>岗位信息</summary>
public class PositionDto : FullAuditedEntityDto<Guid>
{
    public Guid EnterpriseId { get; set; }
    public string? EnterpriseCode { get; set; }
    public string? EnterpriseName { get; set; }
    public EntityReferenceDto? Enterprise { get; set; }
    public Guid DepartmentId { get; set; }
    public string? DepartmentCode { get; set; }
    public string? DepartmentName { get; set; }
    public EntityReferenceDto? Department { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Level { get; set; }
    public string? Description { get; set; }
    public int Order { get; set; }
    public bool IsEnabled { get; set; }
}
