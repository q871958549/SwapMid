using System;

namespace SwapMid.AccountManagement;

/// <summary>为员工开通账号入参。</summary>
public class GenerateEmployeeAccountInput
{
    /// <summary>员工 ID。</summary>
    public Guid EmployeeId { get; set; }
}

/// <summary>员工账号开通结果。</summary>
public class EmployeeAccountProvisionResultDto
{
    /// <summary>员工 ID。</summary>
    public Guid EmployeeId { get; set; }
    /// <summary>开通/绑定后的账号 ID。</summary>
    public Guid? UserId { get; set; }
    /// <summary>账号用户名。</summary>
    public string? UserName { get; set; }
    /// <summary>本次是否新建了账号。</summary>
    public bool Created { get; set; }
    /// <summary>该员工是否此前已绑定账号。</summary>
    public bool AlreadyBound { get; set; }
    /// <summary>处理结果说明。</summary>
    public string Message { get; set; } = string.Empty;
}
