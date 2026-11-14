using System;
using System.Threading.Tasks;

namespace SwapMid.MessageBus;

/// <summary>
/// 投递调度器替身。入队记录存在容器单例 <see cref="MessageDeliveryRecorder"/> 上(而非本类的
/// static 集合),使并行执行的测试集合互不串味——原因详见该类注释。
/// </summary>
public class FakeMessageDeliveryScheduler : IMessageDeliveryScheduler
{
    private readonly MessageDeliveryRecorder _recorder;

    public FakeMessageDeliveryScheduler(MessageDeliveryRecorder recorder)
    {
        _recorder = recorder;
    }

    public Task EnqueueAsync(Guid messageRecordId, TimeSpan? delay = null)
    {
        _recorder.RecordSchedule(messageRecordId, delay);
        return Task.CompletedTask;
    }
}

public record ScheduledMessageDelivery(Guid MessageRecordId, TimeSpan? Delay);
