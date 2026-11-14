using SwapMid.Infrastructure;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace SwapMid.MessageBus;

public class BotWebhookPayloadFormatterTests
{
    [Theory]
    [InlineData("https://hooks.slack.com/services/T00/B00/XXX", null, BotWebhookProviderKind.Slack)]
    [InlineData("https://open.feishu.cn/open-apis/bot/v2/hook/abc", null, BotWebhookProviderKind.Feishu)]
    [InlineData("https://open.larksuite.com/open-apis/bot/v2/hook/abc", null, BotWebhookProviderKind.Feishu)]
    [InlineData("https://oapi.dingtalk.com/robot/send?access_token=abc", null, BotWebhookProviderKind.DingTalk)]
    [InlineData("https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=abc", null, BotWebhookProviderKind.WeCom)]
    [InlineData("https://webhook.example.test/integrations/events", null, BotWebhookProviderKind.Generic)]
    [InlineData("https://webhook.example.test/custom", "slack", BotWebhookProviderKind.Slack)]
    public void DetectProvider_Should_Match_Host_Path_Or_Explicit_Override(
        string url,
        string? explicitProvider,
        BotWebhookProviderKind expected)
    {
        BotWebhookPayloadFormatter.DetectProvider(new Uri(url), explicitProvider).ShouldBe(expected);
    }

    [Fact]
    public void Format_Generic_Should_Emit_Title_Content_Mentions_Metadata_And_Hmac_Header()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var formatted = BotWebhookPayloadFormatter.Format(
            new Uri("https://webhook.example.test/hook"),
            title: "Order Created",
            markdownBody: "Order #1001 ready",
            secret: "top-secret",
            mentions: ["alice", "bob"],
            metadata: new Dictionary<string, string?> { ["topic"] = "ORDER_CREATED" },
            now: now);

        formatted.Provider.ShouldBe(BotWebhookProviderKind.Generic);
        formatted.SignatureHeaderValue.ShouldNotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(formatted.JsonBody);
        doc.RootElement.GetProperty("title").GetString().ShouldBe("Order Created");
        doc.RootElement.GetProperty("content").GetString().ShouldBe("Order #1001 ready");
        doc.RootElement.GetProperty("mentions").GetArrayLength().ShouldBe(2);
        doc.RootElement.GetProperty("metadata").GetProperty("topic").GetString().ShouldBe("ORDER_CREATED");
    }

    [Fact]
    public void Format_DingTalk_With_Secret_Should_Append_Timestamp_And_Sign_Query()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var formatted = BotWebhookPayloadFormatter.Format(
            new Uri("https://oapi.dingtalk.com/robot/send?access_token=tok123"),
            title: "ETL Alert",
            markdownBody: "Pipeline P-01 failed",
            secret: "SEC123456",
            mentions: ["@all"],
            now: now);

        formatted.Provider.ShouldBe(BotWebhookProviderKind.DingTalk);
        formatted.RequestUri.Query.ShouldContain("access_token=tok123");
        formatted.RequestUri.Query.ShouldContain("timestamp=");
        formatted.RequestUri.Query.ShouldContain("sign=");
        using var doc = JsonDocument.Parse(formatted.JsonBody);
        doc.RootElement.GetProperty("msgtype").GetString().ShouldBe("markdown");
        doc.RootElement.GetProperty("at").GetProperty("isAtAll").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void EvaluateResponseBody_Should_Detect_Provider_Error_Codes()
    {
        BotWebhookPayloadFormatter.EvaluateResponseBody(BotWebhookProviderKind.WeCom, "{\"errcode\":0,\"errmsg\":\"ok\"}")
            .Succeeded.ShouldBeTrue();

        var failed = BotWebhookPayloadFormatter.EvaluateResponseBody(
            BotWebhookProviderKind.DingTalk,
            "{\"errcode\":310000,\"errmsg\":\"sign not match\"}");
        failed.Succeeded.ShouldBeFalse();
        failed.ErrorMessage.ShouldNotBeNull();
        failed.ErrorMessage!.ShouldContain("310000");
    }
}
