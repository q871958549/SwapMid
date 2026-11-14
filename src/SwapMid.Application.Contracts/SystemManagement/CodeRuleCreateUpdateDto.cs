using System.ComponentModel.DataAnnotations;
using SwapMid.SystemManagement;

namespace SwapMid.SystemManagement;

/// <summary>编码规则新增/编辑入参。</summary>
public class CodeRuleCreateUpdateDto
{
    /// <summary>适用实体类型（编码规则的业务对象标识）。</summary>
    [Required]
    [MaxLength(CodeRuleConsts.MaxEntityTypeLength)]
    public string EntityType { get; set; } = null!;

    /// <summary>显示名称。</summary>
    [Required]
    [MaxLength(CodeRuleConsts.MaxEntityTypeLength)]
    public string DisplayName { get; set; } = null!;

    /// <summary>编码前缀。</summary>
    [MaxLength(CodeRuleConsts.MaxPrefixLength)]
    public string Prefix { get; set; } = string.Empty;

    /// <summary>编码后缀。</summary>
    [MaxLength(CodeRuleConsts.MaxSuffixLength)]
    public string? Suffix { get; set; }

    /// <summary>编码中是否包含日期段。</summary>
    public bool IncludeDate { get; set; }

    /// <summary>日期段格式（如 yyyyMMdd）。</summary>
    [MaxLength(CodeRuleConsts.MaxDateFormatLength)]
    public string? DateFormat { get; set; }

    /// <summary>流水号长度（不足左侧补零）。</summary>
    [Range(1, 20)]
    public int SequencePad { get; set; } = 4;

    /// <summary>流水号重置周期。</summary>
    public SequenceResetCycle ResetCycle { get; set; } = SequenceResetCycle.Never;

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>描述。</summary>
    [MaxLength(CodeRuleConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}
