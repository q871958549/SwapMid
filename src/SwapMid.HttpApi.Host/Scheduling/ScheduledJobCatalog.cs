using System;
using System.Collections.Generic;
using System.Linq;
using Hangfire;
using SwapMid.MessageBus;
using SwapMid.Scheduling;

namespace SwapMid.HttpApiHost.Scheduling;

/// <summary>
/// 平台所有受管控定时任务的代码目录（key、显示名、默认 cron、注册委托）。
/// Hangfire 存储即任务的「启用」状态：存在=启用，移除=停用。
/// </summary>
public static class ScheduledJobCatalog
{
    public const string AuditLogCleanupKey = ScheduledJobKeys.AuditLogCleanup;
    public const string ExecutionHistoryCleanupKey = ScheduledJobKeys.ExecutionHistoryCleanup;
    public const string MessageRecordCleanupKey = ScheduledJobKeys.MessageRecordCleanup;
    public const string NotificationLogCleanupKey = ScheduledJobKeys.NotificationLogCleanup;

    public static IReadOnlyList<ScheduledJobDefinition> All { get; } = new[]
    {
        new ScheduledJobDefinition
        {
            Key = AuditLogCleanupKey,
            DisplayName = "审计日志定时清理",
            Description = "删除超过保留期的审计日志（缺省保留 90 天，可配 AuditLogCleanup:RetainDays；支持演练预览删除量；默认停用）",
            DefaultCron = "0 3 * * *",
            Register = (manager, cron) => manager.AddOrUpdate<AuditLogCleanupJob>(
                AuditLogCleanupKey, job => job.ExecuteAsync(System.Threading.CancellationToken.None), cron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Local }),
            IsDestructive = true,
        },
        new ScheduledJobDefinition
        {
            Key = ExecutionHistoryCleanupKey,
            DisplayName = "执行历史清理",
            Description = "删除超过保留期的定时任务执行历史（缺省保留 90 天，可配 ScheduledJobExecutionCleanup:RetainDays；仅删终态记录；支持演练预览删除量；默认停用）",
            DefaultCron = "0 4 * * *",
            Register = (manager, cron) => manager.AddOrUpdate<ExecutionHistoryCleanupJob>(
                ExecutionHistoryCleanupKey, job => job.ExecuteAsync(System.Threading.CancellationToken.None), cron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Local }),
            IsDestructive = true,
        },
        new ScheduledJobDefinition
        {
            Key = MessageRecordCleanupKey,
            DisplayName = "消息投递记录清理",
            Description = "删除超过保留期且已终态（已投递/已死信）的消息记录（缺省保留 90 天，可配 MessageRecordCleanup:RetainDays；待投递/重试中记录永不清理；支持演练预览删除量；默认停用）",
            DefaultCron = "15 4 * * *",
            Register = (manager, cron) => manager.AddOrUpdate<MessageRecordCleanupJob>(
                MessageRecordCleanupKey, job => job.ExecuteAsync(System.Threading.CancellationToken.None), cron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Local }),
            IsDestructive = true,
        },
        new ScheduledJobDefinition
        {
            Key = NotificationLogCleanupKey,
            DisplayName = "通知发送日志清理",
            Description = "删除超过保留期的通知中心发送台账（缺省保留 90 天，可配 NotificationLogCleanup:RetainDays；支持演练预览删除量；默认停用）",
            DefaultCron = "30 4 * * *",
            Register = (manager, cron) => manager.AddOrUpdate<NotificationLogCleanupJob>(
                NotificationLogCleanupKey, job => job.ExecuteAsync(System.Threading.CancellationToken.None), cron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Local }),
            IsDestructive = true,
        },
    };

    public static ScheduledJobDefinition? Find(string key)
        => All.FirstOrDefault(x => x.Key == key);
}
