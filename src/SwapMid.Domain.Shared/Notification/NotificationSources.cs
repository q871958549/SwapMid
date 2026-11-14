using System.Collections.Generic;

namespace SwapMid.Notification;

/// <summary>
/// 通知来源（<c>NotificationSendRequest.Source</c>）常量源。
/// 接收人按来源订阅（<c>NotificationRecipient.Sources</c> 逗号分隔存这些值），发送侧与订阅侧必须一致。
/// </summary>
public static class NotificationSources
{
    public const string ScheduledJobs = "定时任务";
    public const string DataIntegration = "数据集成";
    public const string MessageBus = "消息总线";
    public const string NotificationTest = "通知中心 · 测试";
    public const string Alertmanager = "Alertmanager";
    public const string Hangfire = "Hangfire";

    /// <summary>全部已知来源（前端接收人页下拉选项）。</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        ScheduledJobs,
        DataIntegration,
        MessageBus,
        NotificationTest,
        Alertmanager,
        Hangfire,
    };
}
