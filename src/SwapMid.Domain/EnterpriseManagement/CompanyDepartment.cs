using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.EnterpriseManagement;

/// <summary>部门（树形，隶属组织主体）。</summary>
public class CompanyDepartment : FullAuditedAggregateRoot<Guid>
{
    public Guid EnterpriseId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int Level { get; private set; }
    public string? FullPath { get; private set; }
    public Guid? LeaderEmployeeId { get; private set; }
    public int Order { get; private set; }
    public bool IsEnabled { get; private set; }

    private CompanyDepartment() { }

    public CompanyDepartment(
        Guid id,
        Guid enterpriseId,
        string code,
        string name,
        Guid? parentId = null,
        Guid? leaderEmployeeId = null,
        int order = 0)
        : base(id)
    {
        EnterpriseId = enterpriseId;
        ParentId = parentId;
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), CompanyDepartmentConsts.MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), CompanyDepartmentConsts.MaxNameLength);
        Level = parentId.HasValue ? 2 : 1;
        LeaderEmployeeId = leaderEmployeeId;
        Order = order;
        IsEnabled = true;
    }

    public void Update(
        string code,
        string name,
        Guid? parentId,
        Guid? leaderEmployeeId,
        int order)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), CompanyDepartmentConsts.MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), CompanyDepartmentConsts.MaxNameLength);
        ParentId = parentId;
        LeaderEmployeeId = leaderEmployeeId;
        Order = order;
    }

    public void SetLeader(Guid? leaderEmployeeId) => LeaderEmployeeId = leaderEmployeeId;

    public void SetHierarchy(int level, string? fullPath)
    {
        Level = Math.Max(1, level);
        FullPath = Check.Length(fullPath, nameof(fullPath), CompanyDepartmentConsts.MaxFullPathLength);
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
}
