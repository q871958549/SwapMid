using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.Notification.Channels;

/// <summary>
/// 通知渠道抽象。新增渠道（Email/DingTalk/Webhook…）时实现本接口并加
/// <c>[ExposeServices(typeof(INotificationChannel))]</c>，<see cref="NotificationService"/> 会自动收集并路由。
/// </summary>
public interface INotificationChannel
{
    NotificationChannelType ChannelType { get; }

    /// <summary>该渠道是否已启用且配置完整。</summary>
    Task<bool> IsEnabledAsync();

    Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default);
}
