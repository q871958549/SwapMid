using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid;

public class TimeRangePagedAndSortedResultRequestDto : PagedAndSortedResultRequestDto
{
    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public ListQueryTimeType TimeType { get; set; } = ListQueryTimeType.CreationTime;
}
