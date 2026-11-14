using System.Collections.Generic;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SwapMid.Xunit;

/// <summary>
/// 消息缓冲总线(配合 <see cref="RetryTestCase"/>):把一轮执行产生的全部消息暂存,
/// Dispose 时才转发给真总线——使失败轮次可被整体丢弃、只上报最终轮结果。
/// 结构照抄 xunit v2 官方示例仓 samples.xunit 的 RetryFactExample。
/// </summary>
public class DelayedMessageBus : IMessageBus
{
    private readonly IMessageBus _innerBus;
    private readonly List<IMessageSinkMessage> _messages = new();

    public DelayedMessageBus(IMessageBus innerBus)
    {
        _innerBus = innerBus;
    }

    public bool QueueMessage(IMessageSinkMessage message)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }

        // 缓冲期无法得知真实处理结果,恒返回 true 让执行继续(与官方示例一致)。
        return true;
    }

    public void Dispose()
    {
        foreach (var message in _messages)
        {
            _innerBus.QueueMessage(message);
        }
    }
}
