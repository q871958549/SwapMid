using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using SwapMid.Infrastructure;
using SwapMid.Notification.Channels;
using SwapMid.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Volo.Abp.Settings;
using Xunit;

namespace SwapMid.Notification;

public class WebhookNotificationChannelTests
{
    [Fact]
    public void TryValidateWebhookUri_Should_Allow_Known_Bot_Hosts_And_Configured_Allowlist_Over_Https_Only()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ExternalHttpConfigurationKeys.NotificationWebhookAllowedHosts] = "hooks.internal.example.test",
            })
            .Build();

        WebhookNotificationChannel.TryValidateWebhookUri("https://hooks.slack.com/services/T/B/X", config, out _)
            .ShouldBeTrue();
        WebhookNotificationChannel.TryValidateWebhookUri("https://hooks.internal.example.test/notify", config, out _)
            .ShouldBeTrue();
        WebhookNotificationChannel.TryValidateWebhookUri("http://hooks.internal.example.test/notify", config, out _)
            .ShouldBeFalse();
        WebhookNotificationChannel.TryValidateWebhookUri("https://untrusted.evil.example/notify", config, out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task SendAsync_Should_Post_Signed_Generic_Payload_And_Return_Ok()
    {
        var settings = Substitute.For<ISettingProvider>();
        settings.GetOrNullAsync(SwapMidSettings.NotificationWebhookEnabled).Returns("true");
        settings.GetOrNullAsync(SwapMidSettings.NotificationWebhookUrl).Returns("https://hooks.internal.example.test/alert");
        settings.GetOrNullAsync(SwapMidSettings.NotificationWebhookProvider).Returns("generic");
        settings.GetOrNullAsync(SwapMidSettings.NotificationWebhookSecret).Returns("s3cr3t");

        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler
        {
            Handler = (req, _) =>
            {
                captured = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true}"),
                });
            },
        };

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(ExternalHttpClientNames.NotificationWebhook)
            .Returns(new HttpClient(handler));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ExternalHttpConfigurationKeys.NotificationWebhookAllowedHosts] = "hooks.internal.example.test",
                ["App:FrontendUrl"] = "https://app.swapmid.dev",
            })
            .Build();

        var sut = new WebhookNotificationChannel(
            settings,
            httpFactory,
            config,
            NullLogger<WebhookNotificationChannel>.Instance);

        (await sut.IsEnabledAsync()).ShouldBeTrue();
        var result = await sut.SendAsync(new NotificationSendRequest
        {
            Title = "Pipeline Failed",
            Content = "Orders sync failed",
            Level = NotificationLevel.Critical,
            Source = "DataIntegration",
            ActionPath = "/data-integration/pipelines",
        });

        result.Success.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.Headers.Contains("X-SwapMid-Signature").ShouldBeTrue();
    }
}
