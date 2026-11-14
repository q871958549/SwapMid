using System;

namespace SwapMid.EnterpriseManagement;

public class GetCompanyDepartmentListInput : TimeRangePagedAndSortedResultRequestDto
{
    public Guid? EnterpriseId { get; set; }
    public string? EnterpriseCode { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentCode { get; set; }
    public string? Filter { get; set; }
}
