using System;
using Volo.Abp.Application.Dtos;
using SwapMid.SystemManagement;

namespace SwapMid.SystemManagement;

/// <summary>编码规则信息。</summary>
public class CodeRuleDto : FullAuditedEntityDto<Guid>
{
    /// <summary>适用实体类型（编码规则的业务对象标识）。</summary>
    public string EntityType { get; set; } = null!;
    /// <summary>显示名称。</summary>
    public string DisplayName { get; set; } = null!;
    /// <summary>编码前缀。</summary>
    public string Prefix { get; set; } = string.Empty;
    /// <summary>编码后缀。</summary>
    public string? Suffix { get; set; }
    /// <summary>编码中是否包含日期段。</summary>
    public bool IncludeDate { get; set; }
    /// <summary>日期段格式（如 yyyyMMdd）。</summary>
    public string? DateFormat { get; set; }
    /// <summary>流水号长度（不足左侧补零）。</summary>
    public int SequencePad { get; set; }
    /// <summary>流水号重置周期。</summary>
    public SequenceResetCycle ResetCycle { get; set; }
    /// <summary>当前流水号。</summary>
    public long CurrentSequence { get; set; }
    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; set; }
    /// <summary>描述。</summary>
    public string? Description { get; set; }
    /// <summary>编码样例（按当前规则生成的示例）。</summary>
    public string Sample { get; set; } = string.Empty;
}
