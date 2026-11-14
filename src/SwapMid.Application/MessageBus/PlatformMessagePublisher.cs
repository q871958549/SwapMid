using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace SwapMid.MessageBus;

/// <inheritdoc cref="IPlatformMessagePublisher"/>
public class PlatformMessagePublisher : IPlatformMessagePublisher, ITransientDependency
{
    private readonly IMessageTopicRepository _topicRepository;
    private readonly IMessageRecordRepository _recordRepository;
    private readonly IMessageDeliveryScheduler _deliveryScheduler;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<PlatformMessagePublisher> _logger;

    public PlatformMessagePublisher(
        IMessageTopicRepository topicRepository,
        IMessageRecordRepository recordRepository,
        IMessageDeliveryScheduler deliveryScheduler,
        IGuidGenerator guidGenerator,
        ILogger<PlatformMessagePublisher> logger)
    {
        _topicRepository = topicRepository;
        _recordRepository = recordRepository;
        _deliveryScheduler = deliveryScheduler;
        _guidGenerator = guidGenerator;
        _logger = logger;
    }

    public async Task PublishAsync(string topicName, string subject, string payload)
    {
        // 主题缺失 / 禁用 → 静默跳过,不打断业务(D4)。
        var topic = await _topicRepository.FirstOrDefaultAsync(x => x.Name == topicName);
        if (topic is null || !topic.IsEnabled)
        {
            _logger.LogWarning(
                "消息总线:主题 '{Topic}' 不存在或已禁用,事件 '{Subject}' 已跳过(不影响业务)", topicName, subject);
            return;
        }

        subject = Truncate(subject, MessageRecordConsts.MaxSubjectLength);
        if (payload.Length > MessageRecordConsts.MaxPayloadLength)
        {
            _logger.LogWarning(
                "消息总线:事件 '{Subject}' 载荷 {Length} 超上限 {Max},已截断落库",
                subject, payload.Length, MessageRecordConsts.MaxPayloadLength);
            payload = payload[..MessageRecordConsts.MaxPayloadLength];
        }

        // 在调用方当前 UoW 内落记录:业务回滚则本条随之回滚(事务性发件箱,D2)。
        var record = new MessageRecord(_guidGenerator.Create(), topic.Id, topic.Name, subject, payload);
        await _recordRepository.InsertAsync(record, autoSave: true);

        // 入队投递。调度器 UoW 感知——本 UoW 提交后才真正入队(D3),此处直调即可。
        await _deliveryScheduler.EnqueueAsync(record.Id);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
