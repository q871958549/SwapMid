using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.EnterpriseManagement;

/// <summary>岗位主数据（隶属组织主体与部门）。</summary>
public class Position : FullAuditedAggregateRoot<Guid>
{
    public Guid EnterpriseId { get; private set; }
    public Guid DepartmentId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Level { get; private set; }
    public string? Description { get; private set; }
    public int Order { get; private set; }
    public bool IsEnabled { get; private set; }

    private Position() { }

    public Position(
        Guid id,
        Guid enterpriseId,
        Guid departmentId,
        string code,
        string name,
        string? level = null,
        string? description = null,
        int order = 0)
        : base(id)
    {
        EnterpriseId = enterpriseId;
        DepartmentId = departmentId;
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), PositionConsts.MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), PositionConsts.MaxNameLength);
        Level = Check.Length(level, nameof(level), PositionConsts.MaxLevelLength);
        Description = Check.Length(description, nameof(description), PositionConsts.MaxDescriptionLength);
        Order = order;
        IsEnabled = true;
    }

    public void Update(
        Guid departmentId,
        string code,
        string name,
        string? level,
        string? description,
        int order)
    {
        DepartmentId = departmentId;
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), PositionConsts.MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), PositionConsts.MaxNameLength);
        Level = Check.Length(level, nameof(level), PositionConsts.MaxLevelLength);
        Description = Check.Length(description, nameof(description), PositionConsts.MaxDescriptionLength);
        Order = order;
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
}
