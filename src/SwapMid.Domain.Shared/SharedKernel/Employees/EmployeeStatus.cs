using System.ComponentModel;

namespace SwapMid.SharedKernel.Employees;

/// <summary>员工在职状态。</summary>
public enum EmployeeStatus
{
    /// <summary>在职。</summary>
    [Description("在职")]
    Active = 0,

    /// <summary>停薪留职。</summary>
    [Description("停薪留职")]
    Suspended = 1,

    /// <summary>离职。</summary>
    [Description("离职")]
    Resigned = 2,
}
