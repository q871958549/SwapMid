using System;
using SwapMid.SharedKernel.Employees;

namespace SwapMid.EnterpriseManagement;

public class GetEmployeeListInput : TimeRangePagedAndSortedResultRequestDto
{
    public Guid? EnterpriseId { get; set; }
    public string? EnterpriseCode { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? DepartmentCode { get; set; }

    /// <summary>按部门查询时是否包含其全部下级部门（默认 false，向后兼容）。</summary>
    public bool IncludeChildDepartments { get; set; }

    public EmployeeStatus? Status { get; set; }

    /// <summary>按是否已生成登录账号筛选：true=已生成账号，false=未生成账号，null=不限。</summary>
    public bool? HasAccount { get; set; }

    public string? Filter { get; set; }
}
