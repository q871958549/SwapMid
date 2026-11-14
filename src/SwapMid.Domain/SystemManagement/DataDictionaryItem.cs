using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.SystemManagement;

/// <summary>
/// 数据字典项。对标 jeecg-boot sys_dict_item：隶属某个 <see cref="DataDictionary"/>，
/// <see cref="Value"/> 为实际存储值，<see cref="Label"/> 为展示文本。
/// </summary>
public class DataDictionaryItem : FullAuditedAggregateRoot<Guid>
{
    /// <summary>所属字典 Id。</summary>
    public Guid DictionaryId { get; private set; }

    /// <summary>显示文本（如 "男"）。</summary>
    public string Label { get; private set; } = null!;

    /// <summary>实际值（如 "1"）。</summary>
    public string Value { get; private set; } = null!;

    /// <summary>排序号（升序，越小越靠前）。</summary>
    public int SortOrder { get; private set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>描述。</summary>
    public string? Description { get; private set; }

    private DataDictionaryItem() { }

    public DataDictionaryItem(
        Guid id,
        Guid dictionaryId,
        string label,
        string value,
        int sortOrder = 0,
        string? description = null)
        : base(id)
    {
        DictionaryId = dictionaryId;
        Label = Check.NotNullOrWhiteSpace(label, nameof(label), DataDictionaryConsts.MaxItemLabelLength);
        Value = Check.NotNullOrWhiteSpace(value, nameof(value), DataDictionaryConsts.MaxItemValueLength);
        SortOrder = sortOrder;
        Description = description;
        IsEnabled = true;
    }

    public void Update(string label, string value, int sortOrder, string? description)
    {
        Label = Check.NotNullOrWhiteSpace(label, nameof(label), DataDictionaryConsts.MaxItemLabelLength);
        Value = Check.NotNullOrWhiteSpace(value, nameof(value), DataDictionaryConsts.MaxItemValueLength);
        SortOrder = sortOrder;
        Description = description;
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
}
