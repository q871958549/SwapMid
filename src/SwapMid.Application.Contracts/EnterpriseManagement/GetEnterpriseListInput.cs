namespace SwapMid.EnterpriseManagement;

public class GetEnterpriseListInput : TimeRangePagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
    public EnterpriseStatus? Status { get; set; }
}
