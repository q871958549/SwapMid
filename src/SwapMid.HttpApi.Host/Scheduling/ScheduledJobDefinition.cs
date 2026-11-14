using System;
using Hangfire;

namespace SwapMid.HttpApiHost.Scheduling;

/// <summary>一条受管控定时任务的登记项（key、cron、注册委托与风险标记）。</summary>
public sealed class ScheduledJobDefinition
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required string DefaultCron { get; init; }
    public required Action<IRecurringJobManager, string> Register { get; init; }

    /// <summary>危险操作（物理删除数据 / 写外部生产系统）：前端标红并在「立即执行」前二次确认，引导先演练。</summary>
    public bool IsDestructive { get; init; }

    /// <summary>长任务（实时查外部库的报表 / 全量同步，单次可能数十秒到分钟级）：前端「立即执行」默认
    /// 走后台入队（F6，RunInBackground），避免顶着 HTTP 超时；演练仍同步。秒级任务（提醒/清理）不标。</summary>
    public bool IsLongRunning { get; init; }
}
