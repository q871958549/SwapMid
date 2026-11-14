using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.Notification;

/// <summary>
/// 通用通知中心对外发送入口。平台任意模块可注入它发通知（告警是首个使用方）。
/// 实现按渠道（<see cref="NotificationChannelType"/>）路由；未指定渠道时发往所有已启用渠道。
/// 发送失败不抛异常，以 <see cref="NotificationSendResult"/> 返回（通知是旁路，不应拖垮主流程）。
/// </summary>
public interface INotificationService
{
    Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default);
}

public class NotificationSendRequest
{
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public NotificationLevel Level { get; set; } = NotificationLevel.Info;

    /// <summary>接收人；null/空 用渠道默认接收人。多个可用 | 或 , 分隔，或 @all。</summary>
    public string? ToUser { get; set; }

    /// <summary>来源标签（如 "Alertmanager" / "ScheduledJobs"），展示在消息脚注。</summary>
    public string? Source { get; set; }

    /// <summary>目标渠道；null 表示发往所有已启用渠道。</summary>
    public NotificationChannelType? Channel { get; set; }

    /// <summary>附件（仅邮件渠道支持）；用于产出物分发。</summary>
    public List<NotificationAttachment> Attachments { get; set; } = new();

    /// <summary>
    /// 平台内相对路径（须以单个 <c>/</c> 开头）。
    /// 渠道据此拼 <c>App:FrontendUrl</c> 生成可点击链接；禁止传任意绝对 URL。空则不加链接。
    /// </summary>
    public string? ActionPath { get; set; }

    /// <summary>链接锚文本；空则用「打开处理页」。</summary>
    public string? ActionLabel { get; set; }

    /// <summary>恢复通知对应的原故障通知标题（用于闭环关联同一事故区间）。</summary>
    public string? RecoveryOfTitle { get; set; }
}

/// <summary>通知附件（邮件渠道用）。</summary>
public class NotificationAttachment
{
    /// <summary>文件名，含扩展名（如「材料超耗率-202606.xlsx」）。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>文件字节内容。</summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();

    /// <summary>MIME 类型（xlsx = application/vnd.openxmlformats-officedocument.spreadsheetml.sheet）。</summary>
    public string ContentType { get; set; } = "application/octet-stream";
}

public class NotificationSendResult
{
    public bool Success { get; set; }

    public string? Error { get; set; }

    /// <summary>
    /// 配置性跳过（渠道未启用 / 广播无匹配接收人）：没有发出、但不是系统故障。
    /// 与真失败（渠道启用但发送异常）区分——调用方（如定时任务 Runner 的产出分发）据此决定
    /// 「记摘要不告警」还是「记失败并告警」；未配置通知的环境不应因此常态告警。
    /// Skipped=true 时 Success 恒为 false（毕竟没发出去），只看 Success 的既有调用方行为不变。
    /// </summary>
    public bool Skipped { get; set; }

    public static NotificationSendResult Ok() => new() { Success = true };

    public static NotificationSendResult Fail(string error) => new() { Success = false, Error = error };

    /// <summary>配置性跳过（非故障）。</summary>
    public static NotificationSendResult Skip(string reason) => new() { Success = false, Skipped = true, Error = reason };
}
