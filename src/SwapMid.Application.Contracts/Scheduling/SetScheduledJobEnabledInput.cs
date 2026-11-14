using System.ComponentModel.DataAnnotations;

namespace SwapMid.Scheduling;

public class SetScheduledJobEnabledInput
{
    [Required]
    public string Key { get; set; } = null!;

    public bool Enabled { get; set; }

    /// <summary>启用时使用的 cron（5 段标准表达式）；为空则用任务默认 cron。</summary>
    public string? Cron { get; set; }
}
