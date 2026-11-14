using System;

namespace SwapMid.SharedKernel.Employees;

/// <summary>员工主数据只读快照。跨域读取员工信息时不暴露领域实体。</summary>
public sealed record EmployeeSnapshot(
    Guid Id,
    Guid EnterpriseId,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? UserId,
    string EmployeeNo,
    string Name,
    string? Email,
    string? Phone,
    string? PositionText,
    EmployeeStatus Status);
