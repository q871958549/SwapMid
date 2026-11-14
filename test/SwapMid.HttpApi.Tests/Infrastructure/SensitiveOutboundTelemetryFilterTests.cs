using System.Net.Http;
using SwapMid.Infrastructure;
using Shouldly;

namespace SwapMid.HttpApi.Tests.Infrastructure;

public class SensitiveOutboundTelemetryFilterTests
{
    [Fact]
    public void Explicitly_marked_dynamic_webhook_is_excluded_before_span_creation()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://hooks.example.com/delivery?token=must-not-reach-traces");
        SensitiveOutboundRequestOptions.Mark(request);

        SensitiveOutboundTelemetryFilter.ShouldTrace(request).ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://hooks.slack.com/services/T00/B00/must-not-reach-traces")]
    [InlineData("https://open.feishu.cn/open-apis/bot/v2/hook/must-not-reach-traces")]
    [InlineData("https://open.larksuite.com/open-apis/bot/v2/hook/must-not-reach-traces")]
    [InlineData("https://oapi.dingtalk.com/robot/send?access_token=must-not-reach-traces")]
    [InlineData("https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=must-not-reach-traces")]
    public void Secret_bearing_bot_webhook_requests_are_excluded_before_span_creation(string uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);

        SensitiveOutboundTelemetryFilter.ShouldTrace(request).ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://api.example.com/v1/orders")]
    [InlineData("https://hooks.slack.com/status")]
    [InlineData("https://open.feishu.cn/open-apis/authen/v1/index")]
    public void Non_sensitive_outbound_requests_remain_traceable(string uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        SensitiveOutboundTelemetryFilter.ShouldTrace(request).ShouldBeTrue();
    }
}
