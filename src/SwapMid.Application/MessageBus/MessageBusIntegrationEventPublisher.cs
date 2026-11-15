using System;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace SwapMid.MessageBus;

/// <summary>
/// 将跨引擎 <see cref="IntegrationEventEnvelope"/> 幂等落库为 <see cref="MessageRecord"/> 并投递给订阅者。
/// </summary>
[ExposeServices(
    typeof(IIntegrationEventPublisher),
    typeof(MessageBusIntegrationEventPublisher))]
public class MessageBusIntegrationEventPublisher :
    IIntegrationEventPublisher,
    ITransientDependency
{
    private readonly IMessageTopicRepository _topicRepository;
    private readonly IMessageRecordRepository _recordRepository;
    private readonly IMessageDeliveryScheduler _deliveryScheduler;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public MessageBusIntegrationEventPublisher(
        IMessageTopicRepository topicRepository,
        IMessageRecordRepository recordRepository,
        IMessageDeliveryScheduler deliveryScheduler,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _topicRepository = topicRepository;
        _recordRepository = recordRepository;
        _deliveryScheduler = deliveryScheduler;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<bool> PublishIfTopicActiveAsync(
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        using var hostScope = _currentTenant.Change(null);
        using var uow = _unitOfWorkManager.Begin(requiresNew: false, isTransactional: true);

        var topic = await _topicRepository.FirstOrDefaultAsync(
            x => x.Name == envelope.TopicName,
            cancellationToken: cancellationToken);
        if (topic is null || !topic.IsEnabled)
        {
            await uow.CompleteAsync(cancellationToken);
            return false;
        }

        var existing = await _recordRepository.FindAsync(
            envelope.EventId,
            includeDetails: false,
            cancellationToken: cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == MessageStatus.Pending)
            {
                await _deliveryScheduler.EnqueueAsync(existing.Id);
            }

            await uow.CompleteAsync(cancellationToken);
            return true;
        }

        var subject = Truncate(envelope.Subject, MessageRecordConsts.MaxSubjectLength);
        var payload = Truncate(envelope.PayloadJson, MessageRecordConsts.MaxPayloadLength);

        var record = new MessageRecord(
            envelope.EventId,
            topic.Id,
            topic.Name,
            subject,
            payload);

        await _recordRepository.InsertAsync(
            record,
            autoSave: true,
            cancellationToken: cancellationToken);
        await _deliveryScheduler.EnqueueAsync(record.Id);
        await uow.CompleteAsync(cancellationToken);
        return true;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
