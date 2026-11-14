using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Infrastructure;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.MessageBus;

[ExposeServices(typeof(IMessageDeliveryClient))]
public class BotWebhookDeliveryClient : IMessageDeliveryClient, ITransientDependency
{
    private const int MaxResponseInspectBytes = 8192;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMessageDeliveryEndpointPolicy _endpointPolicy;

    public BotWebhookDeliveryClient(
        IHttpClientFactory httpClientFactory,
        IMessageDeliveryEndpointPolicy endpointPolicy)
    {
        _httpClientFactory = httpClientFactory;
        _endpointPolicy = endpointPolicy;
    }

    public DeliveryType DeliveryType => DeliveryType.BotWebhook;

    public async Task<MessageDeliveryResponse> DeliverAsync(
        MessageDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var validatedUri = _endpointPolicy.Validate(DeliveryType.BotWebhook, request.EndpointUrl);
            var bodySummary = $"主题：{request.TopicName}\n订阅：{request.SubscriptionName}\n载荷：{Truncate(request.Payload, 1500)}";
            var formatted = BotWebhookPayloadFormatter.Format(
                validatedUri,
                title: $"[{request.TopicName}] {request.Subject}",
                markdownBody: bodySummary,
                secret: request.Secret,
                metadata: new Dictionary<string, string?>
                {
                    ["messageId"] = request.MessageId.ToString("D"),
                    ["topicName"] = request.TopicName,
                    ["subscriptionName"] = request.SubscriptionName,
                });

            using var content = new StringContent(formatted.JsonBody, Encoding.UTF8, "application/json");
            using var message = new HttpRequestMessage(HttpMethod.Post, formatted.RequestUri) { Content = content };
            SensitiveOutboundRequestOptions.Mark(message);
            message.Headers.TryAddWithoutValidation("X-SwapMid-Message-Id", request.MessageId.ToString("D"));
            message.Headers.TryAddWithoutValidation("X-SwapMid-Topic", request.TopicName);
            if (!string.IsNullOrEmpty(formatted.SignatureHeaderValue))
            {
                message.Headers.TryAddWithoutValidation("X-SwapMid-Signature", formatted.SignatureHeaderValue);
            }

            using var http = _httpClientFactory.CreateClient(ExternalHttpClientNames.MessageDeliveryBotWebhook);
            using var response = await http.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return MessageDeliveryResponse.Failure($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var snippet = await ReadBoundedResponseAsync(response.Content, cancellationToken);
            var (ok, err) = BotWebhookPayloadFormatter.EvaluateResponseBody(formatted.Provider, snippet);
            return ok
                ? MessageDeliveryResponse.Success()
                : MessageDeliveryResponse.Failure(err ?? "Bot Webhook 业务返回非成功状态");
        }
        catch (UserFriendlyException ex)
        {
            return MessageDeliveryResponse.Failure(ex.Message);
        }
        catch (OperationCanceledException)
        {
            return MessageDeliveryResponse.Failure("Bot Webhook 投递超时或已取消");
        }
        catch (HttpRequestException)
        {
            return MessageDeliveryResponse.Failure("Bot Webhook 投递连接失败");
        }
        catch (Exception)
        {
            return MessageDeliveryResponse.Failure("Bot Webhook 投递失败");
        }
    }

    private static string Truncate(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value ?? string.Empty;
        }

        return value[..maxChars] + "...(truncated)";
    }

    private static async Task<string> ReadBoundedResponseAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxResponseInspectBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, total);
    }
}
