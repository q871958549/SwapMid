using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Settings;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;
using Xunit;

namespace SwapMid.Notification;

public class AlertWebhookAppServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-token")]
    public async Task Invalid_Token_Should_Throw_InvalidWebhookToken(string? providedToken)
    {
        var settings = Substitute.For<ISettingProvider>();
        settings.GetOrNullAsync(SwapMidSettings.NotificationAlertWebhookToken).Returns("expected-secret");
        var notifications = Substitute.For<INotificationService>();
        using var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton(settings)
            .BuildServiceProvider();

        var sut = new AlertWebhookAppService(notifications)
        {
            LazyServiceProvider = new AbpLazyServiceProvider(sp),
        };

        var ex = await Should.ThrowAsync<BusinessException>(() =>
            sut.ReceiveAsync(providedToken, new AlertmanagerWebhookInput()));
        ex.Code.ShouldBe(SwapMidDomainErrorCodes.NotificationCenter.InvalidWebhookToken);
        await notifications.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task Valid_Token_Should_Forward_Alerts_To_NotificationCenter()
    {
        var settings = Substitute.For<ISettingProvider>();
        settings.GetOrNullAsync(SwapMidSettings.NotificationAlertWebhookToken).Returns("expected-secret");

        var sent = new List<NotificationSendRequest>();
        var notifications = Substitute.For<INotificationService>();
        notifications.SendAsync(Arg.Any<NotificationSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                sent.Add(ci.Arg<NotificationSendRequest>());
                return NotificationSendResult.Ok();
            });

        using var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton(settings)
            .BuildServiceProvider();

        var sut = new AlertWebhookAppService(notifications)
        {
            LazyServiceProvider = new AbpLazyServiceProvider(sp),
        };

        var res = await sut.ReceiveAsync("expected-secret", new AlertmanagerWebhookInput
        {
            Alerts =
            [
                new AlertmanagerAlertItemDto
                {
                    Status = "firing",
                    Labels = new Dictionary<string, string>
                    {
                        ["alertname"] = "HighErrorRate",
                        ["severity"] = "critical",
                        ["instance"] = "api-01:8080",
                    },
                    Annotations = new Dictionary<string, string>
                    {
                        ["summary"] = "API 5xx spike",
                        ["description"] = "5xx > 5%",
                    },
                }
            ],
        });

        res.Accepted.ShouldBeTrue();
        res.ReceivedCount.ShouldBe(1);
        res.FailedCount.ShouldBe(0);
        sent.Count.ShouldBe(1);
        sent[0].Level.ShouldBe(NotificationLevel.Critical);
        sent[0].Source.ShouldBe(NotificationSources.Alertmanager);
        sent[0].Title.ShouldContain("[FIRING] API 5xx spike");
    }
}
