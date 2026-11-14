using System.ComponentModel.DataAnnotations;

namespace SwapMid.SystemManagement;

/// <summary>数据字典新增/编辑入参。</summary>
public class DataDictionaryCreateUpdateDto
{
    /// <summary>字典编码（业务唯一键，如 gender）。</summary>
    [Required]
    [MaxLength(DataDictionaryConsts.MaxCodeLength)]
    public string Code { get; set; } = null!;

    /// <summary>字典名称。</summary>
    [Required]
    [MaxLength(DataDictionaryConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>描述。</summary>
    [MaxLength(DataDictionaryConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; set; } = true;
}
