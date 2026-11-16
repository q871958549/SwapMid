using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Permissions;
using SwapMid.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.MessageBus;

[Authorize(SwapMidPermissions.MessageBus.Records.Default)]
public class MessageRecordAppService : SwapMidAppService, IMessageRecordAppService
{
    private readonly IMessageRecordRepository _recordRepository;
    private readonly IMessageTopicRepository _topicRepository;
    private readonly IRepository<MessageDeliveryAttempt, Guid> _attemptRepository;
    private readonly IMessageDeliveryScheduler _deliveryScheduler;

    public MessageRecordAppService(
        IMessageRecordRepository recordRepository,
        IMessageTopicRepository topicRepository,
        IRepository<MessageDeliveryAttempt, Guid> attemptRepository,
        IMessageDeliveryScheduler deliveryScheduler)
    {
        _recordRepository = recordRepository;
        _topicRepository = topicRepository;
        _attemptRepository = attemptRepository;
        _deliveryScheduler = deliveryScheduler;
    }

    public async Task<PagedResultDto<MessageRecordDto>> GetListAsync(GetMessageRecordListInput input)
    {
        var totalCount = await _recordRepository.GetCountAsync(
            input.TopicId, input.Status, input.Filter,
            input.StartTime, input.EndTime, input.TimeType);
        var items = await _recordRepository.GetListAsync(
            input.TopicId, input.Status, input.Filter,
            input.StartTime, input.EndTime, input.TimeType,
            input.Sorting, input.MaxResultCount, input.SkipCount);
        return new PagedResultDto<MessageRecordDto>(totalCount, items.Select(MapToDto).ToList());
    }

    public async Task<MessageRecordDto> GetAsync(Guid id)
    {
        var record = await _recordRepository.GetAsync(id);
        return MapToDto(record);
    }

    public async Task<List<MessageDeliveryAttemptDto>> GetAttemptsAsync(Guid id)
    {
        var attempts = await _attemptRepository.GetListAsync(x => x.RecordId == id);
        return attempts
            .OrderBy(a => a.SubscriptionName)
            .Select(a => new MessageDeliveryAttemptDto
            {
                Id = a.Id,
                SubscriptionId = a.SubscriptionId,
                SubscriptionName = a.SubscriptionName,
                Status = a.Status,
                RetryCount = a.RetryCount,
                LastError = a.LastError,
                DeliveredAt = a.DeliveredAt,
                CreationTime = a.CreationTime,
            })
            .ToList();
    }

    [Authorize(SwapMidPermissions.MessageBus.Records.Publish)]
    public async Task<MessageRecordDto> PublishAsync(PublishMessageDto input)
    {
        var topic = await _topicRepository.FindNoTrackingAsync(input.TopicId)
            ?? throw new BusinessException(SwapMidDomainErrorCodes.MessageBus.TopicNotFound);

        var record = new MessageRecord(GuidGenerator.Create(), topic.Id, topic.Name, input.Subject, input.Payload);
        await _recordRepository.InsertAsync(record, autoSave: true);
        await _deliveryScheduler.EnqueueAsync(record.Id);
        return MapToDto(record);
    }

    [Authorize(SwapMidPermissions.MessageBus.Records.Replay)]
    public async Task ReplayAsync(Guid id)
    {
        var record = await _recordRepository.GetAsync(id);
        _ = await _topicRepository.FindNoTrackingAsync(record.TopicId)
            ?? throw new BusinessException(SwapMidDomainErrorCodes.MessageBus.TopicNotFound);

        // 只重置未成功的订阅明细（已成功订阅不重复投递）；无 attempt 的老记录退化为整条重投。
        var attempts = await _attemptRepository.GetListAsync(x => x.RecordId == id);
        if (attempts.Count == 0 && record.Status == MessageStatus.Delivered)
        {
            // 历史 Delivered + 零 attempt 无法区分“当时零订阅”与 attempt 上线前的旧成功记录。
            // 安全侧冻结为空受众，避免 Replay 把后来出现的订阅纳入历史消息；确需重发应发布新 record。
            record.MarkAudienceSnapshotInitialized();
        }

        foreach (var attempt in attempts.Where(a => a.Status != MessageStatus.Delivered))
        {
            attempt.ResetForReplay();
            await _attemptRepository.UpdateAsync(attempt, autoSave: false);
        }

        record.ResetForRetry();
        await _recordRepository.UpdateAsync(record, autoSave: true);
        await _deliveryScheduler.EnqueueAsync(record.Id);
    }

    [Authorize(SwapMidPermissions.MessageBus.Records.Replay)]
    public async Task<BatchOperationResultDto> BatchReplayAsync(BatchIdsInput input)
    {
        var load = await BatchOperationExecutor.LoadAsync(_recordRepository, input.Ids);
        if (load.Found.Count == 0)
        {
            return new BatchOperationResultDto
            {
                Requested = input.Ids.Count,
                Succeeded = 0,
                Skipped = load.MissingCount,
            };
        }

        var foundIds = load.Found.Select(x => x.Id).ToList();
        var topicIds = load.Found.Select(x => x.TopicId).Distinct().ToList();
        var existingTopicIds = await _topicRepository.GetExistingIdsNoTrackingAsync(topicIds);
        var attempts = await _attemptRepository.GetListAsync(x => foundIds.Contains(x.RecordId));
        var attemptsByRecord = attempts.GroupBy(a => a.RecordId).ToDictionary(g => g.Key, g => g.ToList());

        var recordsToUpdate = new List<MessageRecord>(load.Found.Count);
        var attemptsToUpdate = new List<MessageDeliveryAttempt>();
        var skippedMissingTopic = 0;

        foreach (var record in load.Found)
        {
            if (!existingTopicIds.Contains(record.TopicId))
            {
                skippedMissingTopic++;
                continue;
            }

            if (attemptsByRecord.TryGetValue(record.Id, out var recAttempts))
            {
                if (recAttempts.Count == 0 && record.Status == MessageStatus.Delivered)
                {
                    record.MarkAudienceSnapshotInitialized();
                }

                foreach (var attempt in recAttempts.Where(a => a.Status != MessageStatus.Delivered))
                {
                    attempt.ResetForReplay();
                    attemptsToUpdate.Add(attempt);
                }
            }
            else if (record.Status == MessageStatus.Delivered)
            {
                record.MarkAudienceSnapshotInitialized();
            }

            record.ResetForRetry();
            recordsToUpdate.Add(record);
        }

        if (attemptsToUpdate.Count > 0)
        {
            await _attemptRepository.UpdateManyAsync(attemptsToUpdate, autoSave: false);
        }

        if (recordsToUpdate.Count > 0)
        {
            await _recordRepository.UpdateManyAsync(recordsToUpdate, autoSave: true);
            foreach (var record in recordsToUpdate)
            {
                await _deliveryScheduler.EnqueueAsync(record.Id);
            }
        }

        return new BatchOperationResultDto
        {
            Requested = input.Ids.Count,
            Succeeded = recordsToUpdate.Count,
            Skipped = load.MissingCount + skippedMissingTopic,
        };
    }

    private static MessageRecordDto MapToDto(MessageRecord r) => new()
    {
        Id = r.Id, TopicId = r.TopicId, TopicName = r.TopicName,
        Subject = r.Subject, Payload = r.Payload, Status = r.Status,
        RetryCount = r.RetryCount, DeliveredAt = r.DeliveredAt,
        ErrorMessage = r.ErrorMessage, CreationTime = r.CreationTime,
    };
}
