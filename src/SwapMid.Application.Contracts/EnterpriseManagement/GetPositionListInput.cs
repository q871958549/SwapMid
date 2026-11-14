using System;

namespace SwapMid.EnterpriseManagement;

public class GetPositionListInput : TimeRangePagedAndSortedResultRequestDto
{
    public Guid? EnterpriseId { get; set; }
    public string? EnterpriseCode { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? DepartmentCode { get; set; }
    public bool? IsEnabled { get; set; }
    public string? Filter { get; set; }
}
