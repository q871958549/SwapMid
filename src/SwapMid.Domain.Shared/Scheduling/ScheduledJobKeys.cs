namespace SwapMid.Scheduling;

/// <summary>
/// 经 <c>ScheduledJobRunner</c> 驱动的受管控任务 Key 常量源（供 Host 目录、Application 处理器共用）。
/// 值即 Hangfire recurring job id 与执行历史 <c>JobKey</c>，不可随意更改。
/// </summary>
public static class ScheduledJobKeys
{
    public const string ExecutionHistoryCleanup = "ScheduledJobExecution_Cleanup";
    public const string AuditLogCleanup = "AuditLog_Cleanup";
    public const string MessageRecordCleanup = "MessageRecord_Cleanup";
    public const string NotificationLogCleanup = "NotificationLog_Cleanup";
}
