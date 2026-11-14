using System;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.MessageBus;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class MessageTopicAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IMessageTopicAppService _messageTopicAppService;

    public MessageTopicAppServiceTests()
    {
        _messageTopicAppService = GetRequiredService<IMessageTopicAppService>();
    }

    [Fact]
    public async Task Should_Create_And_Filter_Message_Topic()
    {
        var name = $"Topic {Guid.NewGuid():N}";

        var created = await _messageTopicAppService.CreateAsync(new MessageTopicCreateUpdateDto
        {
            Name = name,
            Description = "Order events",
            Type = TopicType.Broadcast,
            RetryCount = 5,
            RetryIntervalSeconds = 60,
            IsEnabled = false
        });

        created.Id.ShouldNotBe(Guid.Empty);
        created.Name.ShouldBe(name);
        created.Type.ShouldBe(TopicType.Broadcast);
        created.RetryCount.ShouldBe(5);
        created.RetryIntervalSeconds.ShouldBe(60);
        created.IsEnabled.ShouldBeFalse();

        var list = await _messageTopicAppService.GetListAsync(new GetMessageTopicListInput
        {
            Filter = name,
            Type = TopicType.Broadcast,
            MaxResultCount = 10
        });

        list.TotalCount.ShouldBe(1);
        list.Items.Single().Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Should_Return_Message_Topics_By_Name()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await _messageTopicAppService.CreateAsync(new MessageTopicCreateUpdateDto
        {
            Name = $"Topic B {suffix}",
            Type = TopicType.Queue
        });
        await _messageTopicAppService.CreateAsync(new MessageTopicCreateUpdateDto
        {
            Name = $"Topic A {suffix}",
            Type = TopicType.Queue
        });

        var list = await _messageTopicAppService.GetListAsync(new GetMessageTopicListInput
        {
            Filter = suffix,
            MaxResultCount = 10
        });

        list.Items.Select(x => x.Name).ToArray().ShouldBe(new[]
        {
            $"Topic A {suffix}",
            $"Topic B {suffix}"
        });
    }

    [Fact]
    public async Task Should_Reject_Duplicate_Message_Topic_Name()
    {
        var name = $"Duplicate Topic {Guid.NewGuid():N}";
        await _messageTopicAppService.CreateAsync(new MessageTopicCreateUpdateDto
        {
            Name = name,
            Type = TopicType.Queue
        });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _messageTopicAppService.CreateAsync(new MessageTopicCreateUpdateDto
            {
                Name = name,
                Type = TopicType.Queue
            }));

        exception.Code.ShouldBe(SwapMidDomainErrorCodes.MessageBus.TopicNameAlreadyExists);
    }
}
