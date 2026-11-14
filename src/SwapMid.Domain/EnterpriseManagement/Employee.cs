using System;
using SwapMid.SharedKernel.Employees;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.EnterpriseManagement;

/// <summary>人员主数据（账号、部门与岗位关联主记录）。</summary>
public class Employee : FullAuditedAggregateRoot<Guid>
{
    public Guid EnterpriseId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public Guid? PositionId { get; private set; }
    public Guid? UserId { get; private set; }
    public string EmployeeNo { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Position { get; private set; }
    public EmployeeStatus Status { get; private set; }

    private Employee() { }

    public Employee(
        Guid id,
        Guid enterpriseId,
        string employeeNo,
        string name,
        Guid? departmentId = null,
        string? email = null,
        string? phone = null,
        string? position = null,
        Guid? userId = null,
        Guid? positionId = null)
        : base(id)
    {
        EnterpriseId = enterpriseId;
        DepartmentId = departmentId;
        PositionId = positionId;
        UserId = userId;
        EmployeeNo = Check.NotNullOrWhiteSpace(employeeNo, nameof(employeeNo), EmployeeConsts.MaxEmployeeNoLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), EmployeeConsts.MaxNameLength);
        Email = Check.Length(email, nameof(email), EmployeeConsts.MaxEmailLength);
        Phone = Check.Length(phone, nameof(phone), EmployeeConsts.MaxPhoneLength);
        Position = Check.Length(position, nameof(position), EmployeeConsts.MaxPositionLength);
        Status = EmployeeStatus.Active;
    }

    public void Update(
        string employeeNo,
        string name,
        Guid? departmentId,
        string? email,
        string? phone,
        string? position,
        Guid? positionId = null)
    {
        DepartmentId = departmentId;
        PositionId = positionId;
        EmployeeNo = Check.NotNullOrWhiteSpace(employeeNo, nameof(employeeNo), EmployeeConsts.MaxEmployeeNoLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), EmployeeConsts.MaxNameLength);
        Email = Check.Length(email, nameof(email), EmployeeConsts.MaxEmailLength);
        Phone = Check.Length(phone, nameof(phone), EmployeeConsts.MaxPhoneLength);
        Position = Check.Length(position, nameof(position), EmployeeConsts.MaxPositionLength);
    }

    public void AssignDepartment(Guid? departmentId) => DepartmentId = departmentId;
    public void BindUser(Guid userId) => UserId = userId;
    public void UnbindUser() => UserId = null;
    public void Activate() => Status = EmployeeStatus.Active;
    public void Suspend() => Status = EmployeeStatus.Suspended;
    public void Resign() => Status = EmployeeStatus.Resigned;
}
