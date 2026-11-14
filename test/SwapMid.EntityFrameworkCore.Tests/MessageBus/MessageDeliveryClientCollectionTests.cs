using System;
using System.Collections.Generic;
using System.Linq;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace SwapMid.MessageBus;

/// <summary>
/// 集合驱动端到端测试：真实容器解析 IEnumerable&lt;IMessageDeliveryClient&gt;，断言每个 DeliveryType
/// 枚举值都有对应实现被收进集合（Http / RabbitMq / BotWebhook）。
/// </summary>
[Trait(SlowTest.CategoryName, SlowTest.CategoryValue)]
[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class MessageDeliveryClientCollectionTests : SwapMidApplicationTestBase<MessageDeliveryClientCollectionTestModule>
{
    private readonly IEnumerable<IMessageDeliveryClient> _deliveryClients;

    public MessageDeliveryClientCollectionTests()
    {
        _deliveryClients = GetRequiredService<IEnumerable<IMessageDeliveryClient>>();
    }

    [Fact]
    public void 集合应收全每个DeliveryType枚举值对应的真实实现()
    {
        var registeredTypes = _deliveryClients.Select(c => c.DeliveryType).ToHashSet();
        var allEnumValues = Enum.GetValues<DeliveryType>().ToHashSet();

        registeredTypes.ShouldBe(allEnumValues, ignoreOrder: true);
    }

    [Fact]
    public void Http投递实现应为HttpMessageDeliveryClient()
    {
        _deliveryClients.OfType<HttpMessageDeliveryClient>().ShouldHaveSingleItem();
    }

    [Fact]
    public void RabbitMq投递实现应为RabbitMqMessageDeliveryClient()
    {
        _deliveryClients.OfType<RabbitMqMessageDeliveryClient>().ShouldHaveSingleItem();
    }

    [Fact]
    public void BotWebhook投递实现应为BotWebhookDeliveryClient()
    {
        _deliveryClients.OfType<BotWebhookDeliveryClient>().ShouldHaveSingleItem();
    }
}
