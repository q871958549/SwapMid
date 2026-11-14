using System;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace SwapMid.MessageBus;

/// <summary>
/// 内部发布口 <see cref="IPlatformMessagePublisher"/> 的机制单测:主题缺失 / 禁用则静默跳过(不落记录、不入队),
/// 主题启用则同 UoW 落记录 + 入队,超长载荷截断。投递调度器在测试模块被替换为 <see cref="FakeMessageDeliveryScheduler"/>。
/// </summary>
[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class PlatformMessagePublisherTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IPlatformMessagePublisher _publisher;
    private readonly IMessageTopicRepository _topicRepository;
    private readonly IMessageRecordRepository _recordRepository;
    private readonly MessageDeliveryRecorder _recorder;

    public PlatformMessagePublisherTests()
    {
        _recorder = GetRequiredService<MessageDeliveryRecorder>();
        _recorder.ResetSchedules();
        _publisher = GetRequiredService<IPlatformMessagePublisher>();
        _topicRepository = GetRequiredService<IMessageTopicRepository>();
        _recordRepository = GetRequiredService<IMessageRecordRepository>();
    }

    [Fact]
    public async Task Should_Skip_When_Topic_Missing()
    {
        await WithUnitOfWorkAsync(() => _publisher.PublishAsync("nonexistent.topic", "evt:1", "{}"));

        _recorder.ScheduledDeliveries.ShouldBeEmpty();
        (await _recordRepository.GetCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Should_Skip_When_Topic_Disabled()
    {
        var topicName = await CreateTopicAsync(enabled: false);

        await WithUnitOfWorkAsync(() => _publisher.PublishAsync(topicName, "evt:1", "{}"));

        _recorder.ScheduledDeliveries.ShouldBeEmpty();
        (await _recordRepository.GetCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Should_Insert_Record_And_Enqueue_When_Enabled()
    {
        var topicName = await CreateTopicAsync(enabled: true);

        await WithUnitOfWorkAsync(() => _publisher.PublishAsync(topicName, "sample.shipped:YP-1", "{\"a\":1}"));

        var record = (await _recordRepository.GetListAsync()).ShouldHaveSingleItem();
        record.TopicName.ShouldBe(topicName);
        record.Subject.ShouldBe("sample.shipped:YP-1");
        record.Payload.ShouldBe("{\"a\":1}");
        record.Status.ShouldBe(MessageStatus.Pending);

        _recorder.ScheduledDeliveries.Single().MessageRecordId.ShouldBe(record.Id);
    }

    [Fact]
    public async Task Should_Truncate_Oversized_Payload()
    {
        var topicName = await CreateTopicAsync(enabled: true);
        var oversized = new string('x', MessageRecordConsts.MaxPayloadLength + 500);

        await WithUnitOfWorkAsync(() => _publisher.PublishAsync(topicName, "evt:1", oversized));

        var record = (await _recordRepository.GetListAsync()).ShouldHaveSingleItem();
        record.Payload.Length.ShouldBe(MessageRecordConsts.MaxPayloadLength);
    }

    private async Task<string> CreateTopicAsync(bool enabled)
    {
        var name = $"test.topic.{Guid.NewGuid():N}";
        await WithUnitOfWorkAsync(async () =>
        {
            var topic = new MessageTopic(Guid.NewGuid(), name, TopicType.Broadcast, "test", retryCount: 3, retryIntervalSeconds: 60);
            if (!enabled)
            {
                topic.Disable();
            }
            await _topicRepository.InsertAsync(topic, autoSave: true);
        });
        return name;
    }
}
