using SwapMid.Infrastructure;
using Shouldly;
using Xunit;

namespace SwapMid.MessageBus;

/// <summary>Webhook HMAC 签名单测:格式、稳定性、对 secret 与 body 的敏感性(防伪造/防篡改)。</summary>
public class WebhookSignatureTests
{
    [Fact]
    public void Should_Produce_Prefixed_Lowercase_Hex()
    {
        var sig = WebhookSignature.Compute("mysecret", "{\"a\":1}");

        sig.ShouldStartWith("sha256=");
        var hex = sig["sha256=".Length..];
        hex.Length.ShouldBe(64); // SHA-256 = 32 字节 = 64 位十六进制
        hex.ShouldBe(hex.ToLowerInvariant());
    }

    [Fact]
    public void Should_Be_Stable_And_Sensitive()
    {
        var sig = WebhookSignature.Compute("mysecret", "{\"a\":1}");

        WebhookSignature.Compute("mysecret", "{\"a\":1}").ShouldBe(sig); // 同 secret+body 稳定
        WebhookSignature.Compute("other", "{\"a\":1}").ShouldNotBe(sig); // 换 secret → 变(防伪造)
        WebhookSignature.Compute("mysecret", "{\"a\":2}").ShouldNotBe(sig); // 改 body → 变(防篡改)
    }
}
