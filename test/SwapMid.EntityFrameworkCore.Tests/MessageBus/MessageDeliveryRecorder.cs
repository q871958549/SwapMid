using System;
using System.Collections.Generic;
using System.Linq;

namespace SwapMid.MessageBus;

/// <summary>
/// 测试期投递记录器:每个测试容器一个实例(在测试模块注册为 Singleton),取代此前
/// <see cref="FakeMessageDeliveryClient"/> / <see cref="FakeMessageDeliveryScheduler"/> 各自持有的
/// 进程级 static 集合。
///
/// 为什么必须是容器实例(2026-07-29 CI 并行化改造的前置条件):xUnit 每个 [Fact] 新建独立容器,
/// 但 static 集合是**进程级**的。此前全部 ABP 测试类同挂一个 collection 串行执行,静态共享侥幸无害;
/// 一旦测试集合之间开始并行,两个方向同时失效——
///   ①写入方远多于断言方:任何触发消息投递的测试都会写进同一份列表(样品生命周期经
///     IPlatformMessagePublisher 入队即是),而断言方只有 MessageBus/样品事件这几个类;
///   ②并发中的断言方还会被对方的 Reset() 清空记录。
/// 两者都产生与被测代码无关的偶发失败。改成容器单例后,记录随容器天然隔离,
/// 无需依赖"所有测试串行"这个昂贵前提。
/// </summary>
public sealed class MessageDeliveryRecorder
{
    private readonly object _sync = new();
    private readonly List<MessageDeliveryRequest> _requests = new();
    private readonly List<ScheduledMessageDelivery> _scheduledDeliveries = new();
    private int _callCount;

    /// <summary>投递响应桩;默认恒成功,测试可改写以模拟失败与重试。</summary>
    public Func<MessageDeliveryRequest, int, MessageDeliveryResponse> Handler { get; set; } =
        (_, _) => MessageDeliveryResponse.Success();

    /// <summary>已发生的投递请求快照(按发生顺序)。</summary>
    public IReadOnlyList<MessageDeliveryRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>已入队的(可带延迟)投递快照(按发生顺序)。</summary>
    public IReadOnlyList<ScheduledMessageDelivery> ScheduledDeliveries
    {
        get
        {
            lock (_sync)
            {
                return _scheduledDeliveries.ToArray();
            }
        }
    }

    /// <summary>记录一次投递并按 <see cref="Handler"/> 返回响应;attempt 序号在本容器内单调递增。</summary>
    public MessageDeliveryResponse RecordDelivery(MessageDeliveryRequest request)
    {
        lock (_sync)
        {
            _callCount++;
            _requests.Add(request);
            return Handler(request, _callCount);
        }
    }

    /// <summary>记录一次投递入队。</summary>
    public void RecordSchedule(Guid messageRecordId, TimeSpan? delay)
    {
        lock (_sync)
        {
            _scheduledDeliveries.Add(new ScheduledMessageDelivery(messageRecordId, delay));
        }
    }

    /// <summary>
    /// 清空投递记录并复位响应桩与 attempt 计数(对应旧 <c>FakeMessageDeliveryClient.Reset()</c>)。
    /// 容器已保证测试间隔离,此方法保留是为测试**中途**的"清空后验证不再新增"语义。
    /// </summary>
    public void ResetDeliveries()
    {
        lock (_sync)
        {
            _requests.Clear();
            Handler = (_, _) => MessageDeliveryResponse.Success();
            _callCount = 0;
        }
    }

    /// <summary>
    /// 清空入队记录(对应旧 <c>FakeMessageDeliveryScheduler.Reset()</c>)。
    /// 同上,保留用于测试中途的"清空后验证不再入队"语义。
    /// </summary>
    public void ResetSchedules()
    {
        lock (_sync)
        {
            _scheduledDeliveries.Clear();
        }
    }
}
