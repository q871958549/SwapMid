using System;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.SystemManagement;

/// <summary>数据字典项新增/编辑入参。</summary>
public class DataDictionaryItemCreateUpdateDto
{
    /// <summary>所属字典 Id（新增时必填；编辑时以路径 id 为准）。</summary>
    public Guid DictionaryId { get; set; }

    /// <summary>显示文本。</summary>
    [Required]
    [MaxLength(DataDictionaryConsts.MaxItemLabelLength)]
    public string Label { get; set; } = null!;

    /// <summary>实际值。</summary>
    [Required]
    [MaxLength(DataDictionaryConsts.MaxItemValueLength)]
    public string Value { get; set; } = null!;

    /// <summary>排序号（升序）。</summary>
    public int SortOrder { get; set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>描述。</summary>
    [MaxLength(DataDictionaryConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}
