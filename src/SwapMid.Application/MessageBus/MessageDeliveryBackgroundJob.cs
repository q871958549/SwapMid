using System;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace SwapMid.MessageBus;

public class MessageDeliveryBackgroundJob : AsyncBackgroundJob<MessageDeliveryJobArgs>, ITransientDependency
{
    private readonly IMessageRecordRepository _recordRepository;
    private readonly IMessageTopicRepository _topicRepository;
    private readonly IMessageDeliveryService _deliveryService;
    private readonly IMessageDeliveryScheduler _deliveryScheduler;

    public MessageDeliveryBackgroundJob(
        IMessageRecordRepository recordRepository,
        IMessageTopicRepository topicRepository,
        IMessageDeliveryService deliveryService,
        IMessageDeliveryScheduler deliveryScheduler)
    {
        _recordRepository = recordRepository;
        _topicRepository = topicRepository;
        _deliveryService = deliveryService;
        _deliveryScheduler = deliveryScheduler;
    }

    [UnitOfWork]
    public override async Task ExecuteAsync(MessageDeliveryJobArgs args)
    {
        var record = await _recordRepository.GetAsync(args.MessageRecordId);
        if (record.Status is MessageStatus.Delivered or MessageStatus.DeadLettered)
        {
            return;
        }

        // 只读主题配置快照，避免并发投递作业误将只读主题卷入 EF ChangeTracker 引发 ConcurrencyStamp 冲突。
        var topic = await _topicRepository.FindNoTrackingAsync(record.TopicId)
            ?? throw new BusinessException(SwapMidDomainErrorCodes.MessageBus.TopicNotFound);

        await _deliveryService.DeliverAsync(record, topic);
        await _recordRepository.UpdateAsync(record, autoSave: true);

        if (record.Status == MessageStatus.Failed)
        {
            await _deliveryScheduler.EnqueueAsync(record.Id, TimeSpan.FromSeconds(Math.Max(1, topic.RetryIntervalSeconds)));
        }
    }
}
