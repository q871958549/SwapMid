using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.SystemManagement;

/// <summary>数据字典（字典类型）信息。</summary>
public class DataDictionaryDto : FullAuditedEntityDto<Guid>
{
    /// <summary>字典编码（业务唯一键）。</summary>
    public string Code { get; set; } = null!;
    /// <summary>字典名称。</summary>
    public string Name { get; set; } = null!;
    /// <summary>描述。</summary>
    public string? Description { get; set; }
    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; set; }
    /// <summary>字典项数量。</summary>
    public int ItemCount { get; set; }
}
