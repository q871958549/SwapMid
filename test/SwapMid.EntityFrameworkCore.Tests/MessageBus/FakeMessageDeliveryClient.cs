using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.MessageBus;

/// <summary>
/// 投递客户端替身。记录与响应桩都存在容器单例 <see cref="MessageDeliveryRecorder"/> 上
/// (而非本类的 static 字段),使并行执行的测试集合互不串味。
/// </summary>
public class FakeMessageDeliveryClient : IMessageDeliveryClient
{
    private readonly MessageDeliveryRecorder _recorder;

    public FakeMessageDeliveryClient(MessageDeliveryRecorder recorder)
        : this(DeliveryType.Http, recorder)
    {
    }

    public FakeMessageDeliveryClient(DeliveryType deliveryType, MessageDeliveryRecorder recorder)
    {
        DeliveryType = deliveryType;
        _recorder = recorder;
    }

    public DeliveryType DeliveryType { get; }

    public Task<MessageDeliveryResponse> DeliverAsync(MessageDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_recorder.RecordDelivery(request));
    }
}
