using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Infrastructure;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.MessageBus;

public class HttpMessageDeliveryClient : IMessageDeliveryClient, ITransientDependency
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMessageDeliveryEndpointPolicy _endpointPolicy;

    public HttpMessageDeliveryClient(
        IHttpClientFactory httpClientFactory,
        IMessageDeliveryEndpointPolicy endpointPolicy)
    {
        _httpClientFactory = httpClientFactory;
        _endpointPolicy = endpointPolicy;
    }

    public DeliveryType DeliveryType => DeliveryType.Http;

    public async Task<MessageDeliveryResponse> DeliverAsync(MessageDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = _endpointPolicy.Validate(DeliveryType.Http, request.EndpointUrl);
            var body = JsonSerializer.Serialize(new
            {
                topicName = request.TopicName,
                subject = request.Subject,
                payload = request.Payload,
                subscriptionName = request.SubscriptionName
            });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");

            // 幂等头:消费者按 X-SwapMid-Message-Id 去重(缺陷A 修复前重复投递必然存在,修复后仍是 webhook 工程礼仪,D8)。
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            // 动态 webhook 的 query 可能承载 token；在 Activity 创建前整条排除，不能只依赖日志过滤。
            SensitiveOutboundRequestOptions.Mark(message);
            message.Headers.TryAddWithoutValidation("X-SwapMid-Message-Id", request.MessageId.ToString());
            message.Headers.TryAddWithoutValidation("X-SwapMid-Topic", request.TopicName);
            // 订阅配了密钥则加 HMAC 签名头(对原始 body 签),消费者验真(D11)。
            if (!string.IsNullOrEmpty(request.Secret))
            {
                message.Headers.TryAddWithoutValidation("X-SwapMid-Signature", WebhookSignature.Compute(request.Secret, body));
            }

            using var http = _httpClientFactory.CreateClient(ExternalHttpClientNames.MessageDeliveryHttp);
            // 对端不受平台控制，仅读响应头；默认 ResponseContentRead 会先把任意大响应完整缓冲进内存。
            using var response = await http.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return MessageDeliveryResponse.Success();
            }

            return MessageDeliveryResponse.Failure(
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (UserFriendlyException ex)
        {
            return MessageDeliveryResponse.Failure(ex.Message);
        }
        catch (OperationCanceledException)
        {
            return MessageDeliveryResponse.Failure("HTTP 投递超时或已取消");
        }
        catch (HttpRequestException)
        {
            return MessageDeliveryResponse.Failure("HTTP 投递连接失败");
        }
        catch (Exception)
        {
            return MessageDeliveryResponse.Failure("HTTP 投递失败");
        }
    }
}
