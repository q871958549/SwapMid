using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.SystemManagement;

/// <summary>数据字典项信息。</summary>
public class DataDictionaryItemDto : FullAuditedEntityDto<Guid>
{
    /// <summary>所属字典 Id。</summary>
    public Guid DictionaryId { get; set; }
    /// <summary>显示文本。</summary>
    public string Label { get; set; } = null!;
    /// <summary>实际值。</summary>
    public string Value { get; set; } = null!;
    /// <summary>排序号（升序）。</summary>
    public int SortOrder { get; set; }
    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; set; }
    /// <summary>描述。</summary>
    public string? Description { get; set; }
}
