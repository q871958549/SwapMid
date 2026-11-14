using System;
using System.Collections.Generic;

namespace SwapMid.SystemManagement;

/// <summary>
/// 系统体检总报告。总状态 = 所有检查项里最差的一项（Unknown 不拉低总状态）。
/// </summary>
public class SystemHealthDto
{
    /// <summary>总体状态（取所有项里最差；全为未知则总状态为未知）。</summary>
    public HealthStatus OverallStatus { get; set; }

    /// <summary>总体一句话结论（据 OverallStatus 生成，给管理者一眼看的话）。</summary>
    public string OverallConclusion { get; set; } = string.Empty;

    /// <summary>体检时间（服务器本地时间）。</summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>各检查项明细。</summary>
    public List<HealthItemDto> Items { get; set; } = new();
}
