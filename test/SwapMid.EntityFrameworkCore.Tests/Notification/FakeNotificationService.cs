using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.Notification;

/// <summary>
/// <see cref="INotificationService"/> 的测试替身：不走真实渠道，记录请求供断言、按 <see cref="Behavior"/> 返回预设结果。
/// 默认返回 <see cref="NotificationSendResult.Ok"/>——此前测试宿主用真实 NotificationService，Email 渠道 Setting
/// 默认关闭导致 SendAsync 恒返「没有已启用的通知渠道」，任何依赖分发结果语义的断言都无法可控；
/// 注册为单例（见 SwapMidEntityFrameworkCoreTestModule），测试方法与被测服务共享同一实例。
/// 集成测试共享 DI 容器，每个测试开头调 <see cref="Reset"/> 复位，避免相互污染。
/// </summary>
public class FakeNotificationService : INotificationService
{
    /// <summary>已收到的发送请求，供测试断言（标题 / 收件人 / 来源 / 附件等）。</summary>
    public List<NotificationSendRequest> Sent { get; } = new();

    /// <summary>非空时按此委托返回结果（模拟发送失败 / 配置性跳过）；空时返回 Ok。</summary>
    public Func<NotificationSendRequest, NotificationSendResult>? Behavior { get; set; }

    /// <summary>非空时返回故障任务,用于验证调用方对通知基础设施异常的隔离。</summary>
    public Exception? ExceptionToThrow { get; set; }

    public void Reset()
    {
        Sent.Clear();
        Behavior = null;
        ExceptionToThrow = null;
    }

    public Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default)
    {
        Sent.Add(request);
        if (ExceptionToThrow is not null)
        {
            return Task.FromException<NotificationSendResult>(ExceptionToThrow);
        }

        return Task.FromResult(Behavior?.Invoke(request) ?? NotificationSendResult.Ok());
    }
}
