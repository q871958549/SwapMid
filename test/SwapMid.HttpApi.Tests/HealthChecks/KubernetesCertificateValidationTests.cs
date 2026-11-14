using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using k8s;
using Shouldly;
using Xunit;

namespace SwapMid.HttpApi.Tests.HealthChecks;

public class KubernetesCertificateValidationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 自定义CA只能信任该CA签发的服务端证书(bool trustedIssuer)
    {
        using var trustedKey = RSA.Create(2048);
        using var otherKey = RSA.Create(2048);
        using var trustedAuthority = CreateAuthority("Trusted", trustedKey);
        using var otherAuthority = CreateAuthority("Other", otherKey);
        using var serverKey = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var serverCertificate = request.Create(
            trustedIssuer ? trustedAuthority : otherAuthority,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(1),
            RandomNumberGenerator.GetBytes(16));
        using var chain = new X509Chain();

        // 该回调由 Kubernetes 客户端的 TLS 握手使用。两个 CA 均格式正确且有效，
        // 但配置只授权 trustedAuthority，旧版本会把未知 CA 当成可信链。
        Kubernetes.CertificateValidationCallBack(
            new object(),
            new X509Certificate2Collection(trustedAuthority),
            serverCertificate,
            chain,
            SslPolicyErrors.RemoteCertificateChainErrors).ShouldBe(trustedIssuer);
    }

    private static X509Certificate2 CreateAuthority(string name, RSA key)
    {
        var request = new CertificateRequest(
            "CN=" + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddHours(2));
    }
}
