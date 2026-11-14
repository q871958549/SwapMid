using System;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace SwapMid.MessageBus;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class MessageRecordAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IMessageTopicAppService _topicAppService;
    private readonly IMessageSubscriptionAppService _subscriptionAppService;
    private readonly IMessageRecordAppService _recordAppService;
    private readonly IMessageRecordRepository _recordRepository;
    private readonly MessageDeliveryRecorder _recorder;

    public MessageRecordAppServiceTests()
    {
        _recorder = GetRequiredService<MessageDeliveryRecorder>();
        _recorder.ResetDeliveries();
        _recorder.ResetSchedules();
        _topicAppService = GetRequiredService<IMessageTopicAppService>();
        _subscriptionAppService = GetRequiredService<IMessageSubscriptionAppService>();
        _recordAppService = GetRequiredService<IMessageRecordAppService>();
        _recordRepository = GetRequiredService<IMessageRecordRepository>();
    }

    [Fact]
    public async Task Should_Publish_Message_To_Enabled_Subscription()
    {
        var topic = await CreateTopicAsync(TopicType.Broadcast);
        var subscription = await CreateSubscriptionAsync(topic.Id, "orders-webhook");

        var record = await _recordAppService.PublishAsync(new PublishMessageDto
        {
            TopicId = topic.Id,
            Subject = "order.created",
            Payload = "{\"orderId\":\"o-1\"}"
        });

        record.Status.ShouldBe(MessageStatus.Pending);
        _recorder.ScheduledDeliveries.Single().MessageRecordId.ShouldBe(record.Id);

        await ExecuteDeliveryJobAsync(record.Id);
        record = await _recordAppService.GetAsync(record.Id);

        record.Status.ShouldBe(MessageStatus.Delivered);
        record.DeliveredAt.ShouldNotBeNull();
        record.ErrorMessage.ShouldBeNull();

        _recorder.Requests.Count.ShouldBe(1);
        var request = _recorder.Requests.Single();
        request.TopicName.ShouldBe(topic.Name);
        request.SubscriptionName.ShouldBe(subscription.Name);
        request.Subject.ShouldBe("order.created");
        request.Payload.ShouldBe("{\"orderId\":\"o-1\"}");
    }

    [Fact]
    public async Task Should_Deliver_Broadcast_Only_To_Matching_Subscriptions()
    {
        var topic = await CreateTopicAsync(TopicType.Broadcast);
        await CreateSubscriptionAsync(topic.Id, "orders-subscription", "order.created");
        await CreateSubscriptionAsync(topic.Id, "invoice-subscription", "invoice.created");

        var record = await _recordAppService.PublishAsync(new PublishMessageDto
        {
            TopicId = topic.Id,
            Subject = "order.created",
            Payload = "{\"orderId\":\"o-2\"}"
        });

        await ExecuteDeliveryJobAsync(record.Id);
        record = await _recordAppService.GetAsync(record.Id);

        record.Status.ShouldBe(MessageStatus.Delivered);
        _recorder.Requests.Select(x => x.SubscriptionName).ToArray().ShouldBe(new[]
        {
            "orders-subscription"
        });
    }

    [Fact]
    public async Task Should_Deliver_Queue_To_First_Matching_Subscription()
    {
        var topic = await CreateTopicAsync(TopicType.Queue);
        await CreateSubscriptionAsync(topic.Id, "queue-a");
        await CreateSubscriptionAsync(topic.Id, "queue-b");

        var record = await _recordAppService.PublishAsync(new PublishMessageDto
        {
            TopicId = topic.Id,
            Subject = "queue.message",
            Payload = "{}"
        });

        await ExecuteDeliveryJobAsync(record.Id);
        record = await _recordAppService.GetAsync(record.Id);

        record.Status.ShouldBe(MessageStatus.Delivered);
        _recorder.Requests.Select(x => x.SubscriptionName).ToArray().ShouldBe(new[]
        {
            "queue-a"
        });
    }

    [Fact]
    public async Task Should_Mark_Message_As_DeadLettered_When_Delivery_Fails()
    {
        _recorder.Handler = (_, attempt) =>
            MessageDeliveryResponse.Failure($"endpoint unavailable #{attempt}");

        var topic = await CreateTopicAsync(TopicType.Broadcast, retryCount: 2);
        await CreateSubscriptionAsync(topic.Id, "failing-subscription");

        var record = await _recordAppService.PublishAsync(new PublishMessageDto
        {
            TopicId = topic.Id,
            Subject = "order.failed",
            Payload = "{}"
        });

        await ExecuteDeliveryJobAsync(record.Id);
        record = await _recordAppService.GetAsync(record.Id);

        record.Status.ShouldBe(MessageStatus.Failed);
        record.RetryCount.ShouldBe(1);
        record.ErrorMessage.ShouldNotBeNull();
        record.ErrorMessage.ShouldContain("attempt 1/3");
        _recorder.ScheduledDeliveries.Last().Delay.ShouldBe(TimeSpan.FromSeconds(1));

        await ExecuteDeliveryJobAsync(record.Id);
        record = await _recordAppService.GetAsync(record.Id);
        record.Status.ShouldBe(MessageStatus.Failed);
        record.RetryCount.ShouldBe(2);

        await ExecuteDeliveryJobAsync(record.Id);
        record = await _recordAppService.GetAsync(record.Id);

        record.Status.ShouldBe(MessageStatus.DeadLettered);
        record.RetryCount.ShouldBe(3);
        record.DeliveredAt.ShouldBeNull();
        record.ErrorMessage.ShouldNotBeNull();
        record.ErrorMessage.ShouldContain("attempt 3/3");
        _recorder.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Should_Replay_DeadLettered_Message()
    {
        _recorder.Handler = (_, _) => MessageDeliveryResponse.Failure("first failure");
        var topic = await CreateTopicAsync(TopicType.Broadcast, retryCount: 0);
        await CreateSubscriptionAsync(topic.Id, "replay-subscription");
        var record = await _recordAppService.PublishAsync(new PublishMessageDto
        {
            TopicId = topic.Id,
            Subject = "order.replay",
            Payload = "{}"
        });
        await ExecuteDeliveryJobAsync(record.Id);
        record = await _recordAppService.GetAsync(record.Id);
        record.Status.ShouldBe(MessageStatus.DeadLettered);

        _recorder.ResetDeliveries();
        _recorder.ResetSchedules();

        await _recordAppService.ReplayAsync(record.Id);
        _recorder.ScheduledDeliveries.Single().MessageRecordId.ShouldBe(record.Id);
        await ExecuteDeliveryJobAsync(record.Id);
        var replayed = await _recordAppService.GetAsync(record.Id);

        replayed.Status.ShouldBe(MessageStatus.Delivered);
        replayed.DeliveredAt.ShouldNotBeNull();
        replayed.ErrorMessage.ShouldBeNull();
        _recorder.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Retry_Only_Failed_Subscription_Not_Succeeded_One()
    {
        // 缺陷A 回归:一订阅成功、一订阅失败,重试只补投失败者——已成功订阅绝不被重复投递。
        _recorder.Handler = (req, _) =>
            req.SubscriptionName == "sub-fail"
                ? MessageDeliveryResponse.Failure("boom")
                : MessageDeliveryResponse.Success();

        var topic = await CreateTopicAsync(TopicType.Broadcast, retryCount: 1); // maxAttempts = 2
        await CreateSubscriptionAsync(topic.Id, "sub-fail");
        await CreateSubscriptionAsync(topic.Id, "sub-ok");

        var record = await _recordAppService.PublishAsync(new PublishMessageDto
        {
            TopicId = topic.Id,
            Subject = "evt",
            Payload = "{}"
        });

        // 第一轮:sub-ok 成功、sub-fail 失败待重试 → record Failed。
        await ExecuteDeliveryJobAsync(record.Id);
        record = await _recordAppService.GetAsync(record.Id);
        record.Status.ShouldBe(MessageStatus.Failed);
        RequestsTo("sub-ok").ShouldBe(1);
        RequestsTo("sub-fail").ShouldBe(1);

        // 第二轮:sub-ok 已成功被跳过,sub-fail 再失败达上限死信 → record DeadLettered。
        await ExecuteDeliveryJobAsync(record.Id);
        record = await _recordAppService.GetAsync(record.Id);
        record.Status.ShouldBe(MessageStatus.DeadLettered);
        RequestsTo("sub-ok").ShouldBe(1);   // ← 关键:已成功订阅未被重复投递
        RequestsTo("sub-fail").ShouldBe(2);

        var attempts = await _recordAppService.GetAttemptsAsync(record.Id);
        attempts.Single(a => a.SubscriptionName == "sub-ok").Status.ShouldBe(MessageStatus.Delivered);
        attempts.Single(a => a.SubscriptionName == "sub-fail").Status.ShouldBe(MessageStatus.DeadLettered);
    }

    [Fact]
    public async Task Should_Terminalize_Disabled_Queue_Target_Without_Failing_Over()
    {
        _recorder.Handler = (_, _) => MessageDeliveryResponse.Failure("first endpoint unavailable");
        var topic = await CreateTopicAsync(TopicType.Queue, retryCount: 5);
        var first = await CreateSubscriptionAsync(topic.Id, "queue-a");
        await CreateSubscriptionAsync(topic.Id, "queue-b");

        var record = await _recordAppService.PublishAsync(new PublishMessageDto
        {
            TopicId = topic.Id,
            Subject = "queue.message",
            Payload = "{}"
        });
        await ExecuteDeliveryJobAsync(record.Id);
        (await _recordAppService.GetAsync(record.Id)).Status.ShouldBe(MessageStatus.Failed);

        await UpdateSubscriptionAsync(first, isEnabled: false, filterRule: first.FilterRule);
        _recorder.ResetSchedules();
        await ExecuteDeliveryJobAsync(record.Id);

        record = await _recordAppService.GetAsync(record.Id);
        record.Status.ShouldBe(MessageStatus.DeadLettered);
        _recorder.ScheduledDeliveries.ShouldBeEmpty();
        RequestsTo("queue-a").ShouldBe(1);
        RequestsTo("queue-b").ShouldBe(0);

        var attempt = (await _recordAppService.GetAttemptsAsync(record.Id)).Single();
        attempt.SubscriptionId.ShouldBe(first.Id);
        attempt.Status.ShouldBe(MessageStatus.DeadLettered);
        attempt.RetryCount.ShouldBe(1);
        attempt.LastError.ShouldNotBeNull();
        attempt.LastError.ShouldContain("(disabled)");

        // 恢复原订阅后显式 Replay 仍可使用原 attempt；不会改投另一个 Queue 订阅。
        await UpdateSubscriptionAsync(first, isEnabled: true, filterRule: first.FilterRule);
        _recorder.ResetDeliveries();
        _recorder.ResetSchedules();
        await _recordAppService.ReplayAsync(record.Id);
        await ExecuteDeliveryJobAsync(record.Id);

        record = await _recordAppService.GetAsync(record.Id);
        record.Status.ShouldBe(MessageStatus.Delivered);
        RequestsTo("queue-a").ShouldBe(1);
        RequestsTo("queue-b").ShouldBe(0);
        attempt = (await _recordAppService.GetAttemptsAsync(record.Id)).Single();
        attempt.Status.ShouldBe(MessageStatus.Delivered);
        attempt.RetryCount.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Terminalize_Failed_Attempt_When_Subscription_Deleted()
    {
        _recorder.Handler = (_, _) => MessageDeliveryResponse.Failure("endpoint unavailable");
        var topic = await CreateTopicAsync(TopicType.Broadcast, retryCount: 5);
        var subscription = await CreateSubscriptionAsync(topic.Id, "deleted-subscription");
        var record = await _recordAppService.PublishAsync(new PublishMessageDto
        {
            TopicId = topic.Id,
            Subject = "event.deleted-target",
            Payload = "{}"
        });
        await ExecuteDeliveryJobAsync(record.Id);

        await _subscriptionAppService.DeleteAsync(subscription.Id);
        _recorder.ResetSchedules();
        await ExecuteDeliveryJobAsync(record.Id);

        record = await _recordAppService.GetAsync(record.Id);
        record.Status.ShouldBe(MessageStatus.DeadLettered);
        _recorder.ScheduledDeliveries.ShouldBeEmpty();
        RequestsTo(subscription.Name).ShouldBe(1);

        var attempt = (await _recordAppService.GetAttemptsAsync(record.Id)).Single();
        attempt.Status.ShouldBe(MessageStatus.DeadLettered);
        attempt.RetryCount.ShouldBe(1);
        attempt.LastError.ShouldNotBeNull();
        attempt.LastError.ShouldContain("(deleted)");
    }

    [Fact]
    public async Task Should_Terminalize_Failed_Attempt_When_Filter_No_Longer_Matches()
    {
        _recorder.Handler = (_, _) => MessageDeliveryResponse.Failure("endpoint unavailable");
        var topic = await CreateTopicAsync(TopicType.Broadcast, retryCount: 5);
        var subscription = await CreateSubscriptionAsync(topic.Id, "filtered-subscription");
        var record = await _recordAppService.PublishAsync(new PublishMessageDto
        {
            TopicId = topic.Id,
            Subject = "order.created",
            Payload = "{}"
        });
        await ExecuteDeliveryJobAsync(record.Id);

        await UpdateSubscriptionAsync(subscription, isEnabled: true, filterRule: "invoice.created");
        _recorder.ResetSchedules();
        await ExecuteDeliveryJobAsync(record.Id);

        record = await _recordAppService.GetAsync(record.Id);
        record.Status.ShouldBe(MessageStatus.DeadLettered);
        _recorder.ScheduledDeliveries.ShouldBeEmpty();
        RequestsTo(subscription.Name).ShouldBe(1);

        var attempt = (await _recordAppService.GetAttemptsAsync(record.Id)).Single();
        attempt.Status.ShouldBe(MessageStatus.DeadLettered);
        attempt.RetryCount.ShouldBe(1);
        attempt.LastError.ShouldNotBeNull();
        attempt.LastError.ShouldContain("(filter_mismatch)");
    }

    [Fact]
    public async Task Should_Not_Add_New_Broadcast_Target_During_Retry()
    {
        _recorder.Handler = (_, _) => MessageDeliveryResponse.Failure("first failure");
        var topic = await CreateTopicAsync(TopicType.Broadcast, retryCount: 2);
        var original = await CreateSubscriptionAsync(topic.Id, "original-subscription");
        var late = await CreateSubscriptionAsync(topic.Id, "late-subscription", "future-only");
        var record = await _recordAppService.PublishAsync(new PublishMessageDto
        {
            TopicId = topic.Id,
            Subject = "order.created",
            Payload = "{}"
        });
        await ExecuteDeliveryJobAsync(record.Id);

        await UpdateSubscriptionAsync(late, isEnabled: true, filterRule: null);
        _recorder.Handler = (_, _) => MessageDeliveryResponse.Success();
        _recorder.ResetSchedules();
        await ExecuteDeliveryJobAsync(record.Id);

        record = await _recordAppService.GetAsync(record.Id);
        record.Status.ShouldBe(MessageStatus.Delivered);
        _recorder.ScheduledDeliveries.ShouldBeEmpty();
        RequestsTo(original.Name).ShouldBe(2);
        RequestsTo(late.Name).ShouldBe(0);

        var attempts = await _recordAppService.GetAttemptsAsync(record.Id);
        attempts.Count.ShouldBe(1);
        attempts.Single().SubscriptionId.ShouldBe(original.Id);
    }

    [Fact]
    public async Task Should_Keep_Empty_Audience_Frozen_During_Replay()
    {
        var topic = await CreateTopicAsync(TopicType.Broadcast);
        var record = await _recordAppService.PublishAsync(new PublishMessageDto
        {
            TopicId = topic.Id,
            Subject = "event.no-audience",
            Payload = "{}"
        });
        await ExecuteDeliveryJobAsync(record.Id);

        record = await _recordAppService.GetAsync(record.Id);
        record.Status.ShouldBe(MessageStatus.Delivered);
        (await _recordAppService.GetAttemptsAsync(record.Id)).ShouldBeEmpty();
        _recorder.Requests.ShouldBeEmpty();
        var stored = await WithUnitOfWorkAsync(() => _recordRepository.GetAsync(record.Id));
        stored.IsAudienceSnapshotInitialized.ShouldBeTrue();

        await CreateSubscriptionAsync(topic.Id, "late-subscription");
        _recorder.ResetDeliveries();
        _recorder.ResetSchedules();
        await _recordAppService.ReplayAsync(record.Id);
        await ExecuteDeliveryJobAsync(record.Id);

        record = await _recordAppService.GetAsync(record.Id);
        record.Status.ShouldBe(MessageStatus.Delivered);
        (await _recordAppService.GetAttemptsAsync(record.Id)).ShouldBeEmpty();
        _recorder.Requests.ShouldBeEmpty();
        _recorder.ScheduledDeliveries.Count.ShouldBe(1);
    }

    private int RequestsTo(string subscriptionName)
        => _recorder.Requests.Count(r => r.SubscriptionName == subscriptionName);

    private Task<MessageTopicDto> CreateTopicAsync(TopicType type, int retryCount = 3)
    {
        return _topicAppService.CreateAsync(new MessageTopicCreateUpdateDto
        {
            Name = $"Topic {Guid.NewGuid():N}",
            Type = type,
            RetryCount = retryCount,
            RetryIntervalSeconds = 1
        });
    }

    private Task<MessageSubscriptionDto> CreateSubscriptionAsync(Guid topicId, string name, string? filterRule = null)
    {
        return _subscriptionAppService.CreateAsync(new MessageSubscriptionCreateUpdateDto
        {
            TopicId = topicId,
            Name = name,
            EndpointUrl = $"https://example.test/{name}",
            FilterRule = filterRule
        });
    }

    private Task<MessageSubscriptionDto> UpdateSubscriptionAsync(
        MessageSubscriptionDto subscription,
        bool isEnabled,
        string? filterRule)
    {
        return _subscriptionAppService.UpdateAsync(subscription.Id, new MessageSubscriptionCreateUpdateDto
        {
            TopicId = subscription.TopicId,
            Name = subscription.Name,
            DeliveryType = subscription.DeliveryType,
            EndpointUrl = subscription.EndpointUrl,
            ExchangeName = subscription.ExchangeName,
            RoutingKey = subscription.RoutingKey,
            FilterRule = filterRule,
            IsEnabled = isEnabled
        });
    }

    private Task ExecuteDeliveryJobAsync(Guid recordId)
    {
        return GetRequiredService<MessageDeliveryBackgroundJob>()
            .ExecuteAsync(new MessageDeliveryJobArgs { MessageRecordId = recordId });
    }
}
