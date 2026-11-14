using System.Threading.Tasks;

namespace SwapMid.MessageBus;

/// <summary>
/// 平台内部消息发布口——供各业务模块把领域事件投进消息总线。
/// 与 UI 手动发布口 <c>MessageRecordAppService.PublishAsync</c>(按 TopicId + 权限)不同:
/// 本口按主题<b>名称</b>解析、无鉴权(系统触发)、<b>在调用方当前 UoW 内落记录</b>(事务性发件箱——
/// 业务回滚则消息不留),事务提交后才入队投递。总线不可用 / 主题缺失只记日志,<b>绝不把异常抛给业务主流程</b>。
/// </summary>
public interface IPlatformMessagePublisher
{
    /// <param name="topicName">目标主题名(见 <see cref="MessageBusTopics"/>);不存在或已禁用则静默跳过。</param>
    /// <param name="subject">消息标题,建议 <c>{event}:{业务单号}</c>(供订阅方 FilterRule 子串过滤)。</param>
    /// <param name="payload">消息载荷(通常 JSON);超上限自动截断。</param>
    Task PublishAsync(string topicName, string subject, string payload);
}
