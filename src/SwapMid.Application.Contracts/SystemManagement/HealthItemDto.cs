namespace SwapMid.SystemManagement;

/// <summary>
/// 系统体检单项结果。面向非技术管理者：说「影响」不说「零件」。
/// </summary>
public class HealthItemDto
{
    /// <summary>检查项中文名称（如「网站可访问」「服务器内存」）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>健康状态：正常 / 需留意 / 异常 / 未知。</summary>
    public HealthStatus Status { get; set; }

    /// <summary>人话结论（如「运行正常，员工可正常访问」）。</summary>
    public string Conclusion { get; set; } = string.Empty;

    /// <summary>出问题时对业务的影响（如「新提交的订单可能延迟处理」）。正常时可为空。</summary>
    public string? Impact { get; set; }

    /// <summary>处置建议（如「通常几分钟自动恢复；超过 10 分钟仍异常请通知 IT」）。正常时可为空。</summary>
    public string? Suggestion { get; set; }

    /// <summary>可选：原始数值/明细（如「使用率 78%」），供需要细节者参考。</summary>
    public string? Detail { get; set; }
}
