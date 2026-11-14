using System.Net;
using System.Net.Http.Json;
using SwapMid.Notification;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace SwapMid.HttpApi.Tests.Notification;

public class AlertWebhookHttpContractTests
{
    [Theory]
    [InlineData(null, "header-token", "header-token", HttpStatusCode.OK)]
    [InlineData("query-token", null, "query-token", HttpStatusCode.OK)]
    [InlineData("same-token", "same-token", "same-token", HttpStatusCode.OK)]
    [InlineData("query-a", "header-b", null, HttpStatusCode.Unauthorized)]
    public async Task Alertmanager_Webhook_Should_Resolve_Header_And_Query_Tokens_Deterministically(
        string? queryToken,
        string? headerToken,
        string? expectedToken,
        HttpStatusCode expectedStatus)
    {
        var appService = Substitute.For<IAlertWebhookAppService>();
        appService.ReceiveAsync(Arg.Any<string?>(), Arg.Any<AlertmanagerWebhookInput>())
            .Returns(new AlertWebhookReceiveResultDto
            {
                Accepted = true,
                ReceivedCount = 0,
                FailedCount = 0,
                Message = "no alerts",
            });

        using var host = new ContractTestHost(configureServices: services =>
        {
            services.AddSingleton(appService);
        });
        using var client = host.CreateClient(authenticated: false);
        if (headerToken is not null)
        {
            client.DefaultRequestHeaders.Add("X-SwapMid-Alert-Token", headerToken);
        }

        var url = "/api/v1/notification/webhooks/alertmanager" +
                  (queryToken is null ? string.Empty : $"?token={Uri.EscapeDataString(queryToken)}");
        using var response = await client.PostAsJsonAsync(url, new AlertmanagerWebhookInput());

        response.StatusCode.ShouldBe(expectedStatus);
        if (expectedToken is null)
        {
            await appService.DidNotReceiveWithAnyArgs().ReceiveAsync(default, default!);
        }
        else
        {
            await appService.Received(1).ReceiveAsync(expectedToken, Arg.Any<AlertmanagerWebhookInput>());
        }
    }
}
