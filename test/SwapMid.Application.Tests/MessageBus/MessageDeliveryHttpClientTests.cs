using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SwapMid.Infrastructure;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace SwapMid.MessageBus;

public class MessageDeliveryHttpClientTests
{
    [Fact]
    public async Task Http投递应保持包络幂等头主题头与Hmac签名()
    {
        RequestSnapshot? captured = null;
        var responseContent = new TrackingContent("{}");
        var handler = new StubHttpMessageHandler
        {
            Handler = async (message, cancellationToken) =>
            {
                captured = await RequestSnapshot.CaptureAsync(message, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = responseContent };
            },
        };
        var factory = new RecordingClientFactory(handler);
        var client = new HttpMessageDeliveryClient(factory, CreatePolicy());
        var request = CreateRequest(secret: "delivery-secret");

        var result = await client.DeliverAsync(request);

        result.Succeeded.ShouldBeTrue();
        handler.CallCount.ShouldBe(1);
        factory.RequestedNames.ShouldBe(new[] { ExternalHttpClientNames.MessageDeliveryHttp });
        captured.ShouldNotBeNull();
        captured!.Method.ShouldBe(HttpMethod.Post);
        captured.Uri.ShouldBe(new Uri(request.EndpointUrl!));
        captured.Header("X-SwapMid-Message-Id").ShouldBe(request.MessageId.ToString());
        captured.Header("X-SwapMid-Topic").ShouldBe(request.TopicName);
        captured.Header("X-SwapMid-Signature").ShouldBe(
            WebhookSignature.Compute(request.Secret!, captured.Body));
        captured.Header("ce-specversion").ShouldBe("1.0");
        captured.Header("ce-id").ShouldBe(request.MessageId.ToString());
        captured.Header("ce-source").ShouldBe("urn:swapmid:topic:order.lifecycle");
        captured.Header("ce-type").ShouldBe("io.swapmid.order.shipped");
        captured.Header("ce-subject").ShouldBe(request.Subject);
        captured.Header("ce-time").ShouldBe("2026-04-15T08:30:00.0000000Z");
        captured.ContainsCredential.ShouldBeTrue();
        using var body = JsonDocument.Parse(captured.Body);
        body.RootElement.GetProperty("topicName").GetString().ShouldBe(request.TopicName);
        body.RootElement.GetProperty("subject").GetString().ShouldBe(request.Subject);
        body.RootElement.GetProperty("payload").GetString().ShouldBe(request.Payload);
        body.RootElement.GetProperty("subscriptionName").GetString().ShouldBe(request.SubscriptionName);
        responseContent.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Http投递失败不应把对端响应体写入台账且只请求一次()
    {
        var handler = new StubHttpMessageHandler
        {
            Handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("downstream unavailable"),
            }),
        };
        var factory = new RecordingClientFactory(handler);
        var client = new HttpMessageDeliveryClient(factory, CreatePolicy());

        var result = await client.DeliverAsync(CreateRequest());

        result.Succeeded.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("HTTP 502 Bad Gateway");
        handler.CallCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("{\"errcode\":0,\"errmsg\":\"ok\"}", true, null)]
    [InlineData("{\"errcode\":45009,\"errmsg\":\"rate limit\"}", false, "45009")]
    [InlineData("not-json", true, null)]
    public async Task BotWebhook投递应保持业务码与非Json响应语义(
        string responseBody,
        bool expectedSuccess,
        string? expectedErrorFragment)
    {
        string? requestBody = null;
        var containsCredential = false;
        var handler = new StubHttpMessageHandler
        {
            Handler = async (message, cancellationToken) =>
            {
                containsCredential = SensitiveOutboundRequestOptions.IsMarked(message);
                requestBody = await message.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody),
                };
            },
        };
        var factory = new RecordingClientFactory(handler);
        var client = new BotWebhookDeliveryClient(factory, CreatePolicy());

        var result = await client.DeliverAsync(CreateBotWebhookRequest());

        result.Succeeded.ShouldBe(expectedSuccess);
        if (expectedErrorFragment == null)
        {
            result.ErrorMessage.ShouldBeNull();
        }
        else
        {
            result.ErrorMessage.ShouldNotBeNull();
            result.ErrorMessage!.ShouldContain(expectedErrorFragment);
        }
        handler.CallCount.ShouldBe(1);
        factory.RequestedNames.ShouldBe(new[] { ExternalHttpClientNames.MessageDeliveryBotWebhook });
        requestBody.ShouldNotBeNull();
        containsCredential.ShouldBeTrue();
        using var body = JsonDocument.Parse(requestBody!);
        body.RootElement.GetProperty("msgtype").GetString().ShouldBe("markdown");
        body.RootElement.GetProperty("markdown").GetProperty("content").GetString()!
            .ShouldContain(CreateBotWebhookRequest().TopicName);
    }

    [Fact]
    public async Task BotWebhookHttp失败不应把响应正文写入台账且只请求一次()
    {
        var longBody = new string('x', 350);
        var handler = new StubHttpMessageHandler
        {
            Handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(longBody),
            }),
        };
        var client = new BotWebhookDeliveryClient(new RecordingClientFactory(handler), CreatePolicy());

        var result = await client.DeliverAsync(CreateBotWebhookRequest());

        result.Succeeded.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("HTTP 503 Service Unavailable");
        handler.CallCount.ShouldBe(1);
    }

    private static MessageDeliveryRequest CreateRequest(string? secret = null) => new()
    {
        MessageId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        Secret = secret,
        EndpointUrl = "https://webhook.test/delivery",
        TopicName = "order.lifecycle",
        Subject = "order.shipped:ORD-001",
        Payload = "{\"orderCode\":\"ORD-001\"}",
        SubscriptionName = "测试订阅",
        OccurredAtUtc = new DateTime(2026, 4, 15, 8, 30, 0, DateTimeKind.Utc),
    };

    private static MessageDeliveryRequest CreateBotWebhookRequest()
    {
        var request = CreateRequest();
        return new MessageDeliveryRequest
        {
            MessageId = request.MessageId,
            Secret = request.Secret,
            EndpointUrl = "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=test-key",
            TopicName = request.TopicName,
            Subject = request.Subject,
            Payload = request.Payload,
            SubscriptionName = request.SubscriptionName,
        };
    }

    private static IMessageDeliveryEndpointPolicy CreatePolicy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [MessageDeliveryEndpointPolicy.AllowedHostsKey] = "webhook.test",
            })
            .Build();
        return new MessageDeliveryEndpointPolicy(configuration);
    }

    private sealed class RecordingClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public List<string> RequestedNames { get; } = new();

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri? Uri,
        string Body,
        IReadOnlyDictionary<string, string> Headers,
        bool ContainsCredential)
    {
        public string Header(string name) => Headers[name];

        public static async Task<RequestSnapshot> CaptureAsync(
            HttpRequestMessage message,
            System.Threading.CancellationToken cancellationToken)
        {
            var body = message.Content == null
                ? string.Empty
                : await message.Content.ReadAsStringAsync(cancellationToken);
            var headers = message.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase);
            return new RequestSnapshot(
                message.Method,
                message.RequestUri,
                body,
                headers,
                SensitiveOutboundRequestOptions.IsMarked(message));
        }
    }

    private sealed class TrackingContent(string content) : StringContent(content)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
