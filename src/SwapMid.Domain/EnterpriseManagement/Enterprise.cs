using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.EnterpriseManagement;

/// <summary>组织主体主数据（支持多组织主体，IsMain 标记默认主组织）。</summary>
public class Enterprise : FullAuditedAggregateRoot<Guid>
{
    /// <summary>企业编码（业务唯一键）。</summary>
    public string Code { get; private set; } = null!;

    /// <summary>企业全称。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>企业简称。</summary>
    public string? ShortName { get; private set; }

    /// <summary>联系人。</summary>
    public string? ContactPerson { get; private set; }

    /// <summary>联系电话。</summary>
    public string? ContactPhone { get; private set; }

    /// <summary>邮箱。</summary>
    public string? Email { get; private set; }

    /// <summary>地址。</summary>
    public string? Address { get; private set; }

    /// <summary>状态（正常 / 停用 / 归档）。</summary>
    public EnterpriseStatus Status { get; private set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>是否主企业（全局唯一，作为默认归属）。</summary>
    public bool IsMain { get; private set; }

    private Enterprise() { }

    public Enterprise(
        Guid id,
        string code,
        string name,
        string? shortName = null,
        string? contactPerson = null,
        string? contactPhone = null,
        string? email = null,
        string? address = null)
        : base(id)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), EnterpriseConsts.MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), EnterpriseConsts.MaxNameLength);
        ShortName = Check.Length(shortName, nameof(shortName), EnterpriseConsts.MaxShortNameLength);
        ContactPerson = Check.Length(contactPerson, nameof(contactPerson), EnterpriseConsts.MaxContactPersonLength);
        ContactPhone = Check.Length(contactPhone, nameof(contactPhone), EnterpriseConsts.MaxContactPhoneLength);
        Email = Check.Length(email, nameof(email), EnterpriseConsts.MaxEmailLength);
        Address = Check.Length(address, nameof(address), EnterpriseConsts.MaxAddressLength);
        Status = EnterpriseStatus.Active;
        IsEnabled = true;
    }

    public void Update(
        string code,
        string name,
        string? shortName,
        string? contactPerson,
        string? contactPhone,
        string? email,
        string? address)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), EnterpriseConsts.MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), EnterpriseConsts.MaxNameLength);
        ShortName = Check.Length(shortName, nameof(shortName), EnterpriseConsts.MaxShortNameLength);
        ContactPerson = Check.Length(contactPerson, nameof(contactPerson), EnterpriseConsts.MaxContactPersonLength);
        ContactPhone = Check.Length(contactPhone, nameof(contactPhone), EnterpriseConsts.MaxContactPhoneLength);
        Email = Check.Length(email, nameof(email), EnterpriseConsts.MaxEmailLength);
        Address = Check.Length(address, nameof(address), EnterpriseConsts.MaxAddressLength);
    }

    public void Activate()
    {
        Status = EnterpriseStatus.Active;
        IsEnabled = true;
    }

    public void Suspend()
    {
        Status = EnterpriseStatus.Suspended;
        IsEnabled = false;
    }

    public void Archive()
    {
        Status = EnterpriseStatus.Archived;
        IsEnabled = false;
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
    public void SetAsMain() => IsMain = true;
    public void UnsetMain() => IsMain = false;
}
