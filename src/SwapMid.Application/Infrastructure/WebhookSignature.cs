using System;
using System.Security.Cryptography;
using System.Text;

namespace SwapMid.Infrastructure;

/// <summary>
/// Webhook HMAC-SHA256 签名:<c>X-SwapMid-Signature: sha256=&lt;hex(HMAC(secret, body))&gt;</c>。
/// 消费者用同一 secret 对收到的<b>原始请求体</b>重算并比对,即可验真(防伪造、防篡改)。抽为静态类以便单测。
/// </summary>
public static class WebhookSignature
{
    public static string Compute(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
