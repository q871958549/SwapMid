using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace SwapMid.DataIntegration;

/// <summary>S3 / MinIO 兼容对象存储 AWS Signature Version 4 请求签名器。</summary>
internal static class AwsSigV4Signer
{
    internal const string EmptyPayloadSha256Hex =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    internal static void Sign(
        HttpRequestMessage request,
        string region,
        string accessKey,
        string secretKey,
        string payloadSha256Hex,
        DateTimeOffset? utcNowOverride = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uri = request.RequestUri
            ?? throw new ArgumentException("RequestUri is required for AWS SigV4 signing.", nameof(request));

        var utcNow = (utcNowOverride ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var amzDate = utcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var hostHeader = uri.IsDefaultPort
            ? uri.Host
            : $"{uri.Host}:{uri.Port.ToString(CultureInfo.InvariantCulture)}";

        request.Headers.Host = hostHeader;
        request.Headers.Remove("x-amz-date");
        request.Headers.Remove("x-amz-content-sha256");
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadSha256Hex);

        var canonicalUri = EncodeCanonicalUriPath(uri.AbsolutePath);
        var canonicalQuery = BuildCanonicalQueryString(uri.Query);

        var canonicalHeaders = string.Concat(
            $"host:{hostHeader}\n",
            $"x-amz-content-sha256:{payloadSha256Hex}\n",
            $"x-amz-date:{amzDate}\n");
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";

        var canonicalRequest = string.Join(
            '\n',
            request.Method.Method,
            canonicalUri,
            canonicalQuery,
            canonicalHeaders,
            signedHeaders,
            payloadSha256Hex);

        var canonicalHashHex = ToLowerHex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));
        var scope = $"{dateStamp}/{region}/s3/aws4_request";
        var stringToSign = string.Join(
            '\n',
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            canonicalHashHex);

        var signingKey = DeriveSigningKey(secretKey, dateStamp, region, "s3");
        var signature = ToLowerHex(HmacSha256(signingKey, stringToSign));

        request.Headers.Remove("Authorization");
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"AWS4-HMAC-SHA256 Credential={accessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    internal static string ComputeSha256Hex(byte[] bytes)
        => ToLowerHex(SHA256.HashData(bytes));

    private static string EncodeCanonicalUriPath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath) || absolutePath == "/")
        {
            return "/";
        }

        var segments = absolutePath.Split('/');
        return string.Join('/', segments.Select(s => AwsUrlEncode(Uri.UnescapeDataString(s))));
    }

    private static string BuildCanonicalQueryString(string rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery) || rawQuery == "?")
        {
            return string.Empty;
        }

        var trimmed = rawQuery.TrimStart('?');
        var pairs = new List<(string Key, string Value)>();
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var rawKey = eq >= 0 ? part[..eq] : part;
            var rawVal = eq >= 0 ? part[(eq + 1)..] : string.Empty;
            pairs.Add((
                AwsUrlEncode(Uri.UnescapeDataString(rawKey)),
                AwsUrlEncode(Uri.UnescapeDataString(rawVal))));
        }

        return string.Join(
            '&',
            pairs
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .ThenBy(p => p.Value, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"));
    }

    private static string AwsUrlEncode(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            var ch = (char)b;
            if ((ch >= 'A' && ch <= 'Z') ||
                (ch >= 'a' && ch <= 'z') ||
                (ch >= '0' && ch <= '9') ||
                ch is '-' or '_' or '.' or '~')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('%');
                sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static byte[] DeriveSigningKey(string secretKey, string dateStamp, string region, string service)
    {
        var kSecret = Encoding.UTF8.GetBytes("AWS4" + secretKey);
        var kDate = HmacSha256(kSecret, dateStamp);
        var kRegion = HmacSha256(kDate, region);
        var kService = HmacSha256(kRegion, service);
        return HmacSha256(kService, "aws4_request");
    }

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string ToLowerHex(byte[] bytes)
        => Convert.ToHexString(bytes).ToLowerInvariant();
}
