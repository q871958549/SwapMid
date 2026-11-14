using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.SystemManagement;

/// <summary>
/// 数据字典（字典类型）。对标 jeecg-boot sys_dict：以 <see cref="Code"/> 为业务引用键，
/// 下挂若干 <see cref="DataDictionaryItem"/> 字典项，供全平台下拉/状态翻译复用。
/// </summary>
public class DataDictionary : FullAuditedAggregateRoot<Guid>
{
    /// <summary>字典编码（业务唯一键，如 "gender"、"order_status"）。</summary>
    public string Code { get; private set; } = null!;

    /// <summary>字典名称（显示用，如 "性别"）。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>描述。</summary>
    public string? Description { get; private set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; private set; }

    private DataDictionary() { }

    public DataDictionary(
        Guid id,
        string code,
        string name,
        string? description = null)
        : base(id)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), DataDictionaryConsts.MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), DataDictionaryConsts.MaxNameLength);
        Description = description;
        IsEnabled = true;
    }

    public void Update(string code, string name, string? description)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), DataDictionaryConsts.MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), DataDictionaryConsts.MaxNameLength);
        Description = description;
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
}
